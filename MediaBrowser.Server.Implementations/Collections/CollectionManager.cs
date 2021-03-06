﻿using MediaBrowser.Common.IO;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoreLinq;

namespace MediaBrowser.Server.Implementations.Collections
{
    public class CollectionManager : ICollectionManager
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryMonitor _iLibraryMonitor;

        public CollectionManager(ILibraryManager libraryManager, IFileSystem fileSystem, ILibraryMonitor iLibraryMonitor)
        {
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _iLibraryMonitor = iLibraryMonitor;
        }

        public async Task<BoxSet> CreateCollection(CollectionCreationOptions options)
        {
            var name = options.Name;

            // Need to use the [boxset] suffix
            // If internet metadata is not found, or if xml saving is off there will be no collection.xml
            // This could cause it to get re-resolved as a plain folder
            var folderName = _fileSystem.GetValidFilename(name) + " [boxset]";

            var parentFolder = GetParentFolder(options.ParentId);

            if (parentFolder == null)
            {
                throw new ArgumentException();
            }

            var path = Path.Combine(parentFolder.Path, folderName);

            _iLibraryMonitor.ReportFileSystemChangeBeginning(path);

            try
            {
                Directory.CreateDirectory(path);

                var collection = new BoxSet
                {
                    Name = name,
                    Parent = parentFolder,
                    DisplayMediaType = "Collection",
                    Path = path,
                    IsLocked = options.IsLocked,
                    ProviderIds = options.ProviderIds
                };

                await parentFolder.AddChild(collection, CancellationToken.None).ConfigureAwait(false);

                await collection.RefreshMetadata(new MetadataRefreshOptions(), CancellationToken.None)
                    .ConfigureAwait(false);

                if (options.ItemIdList.Count > 0)
                {
                    await AddToCollection(collection.Id, options.ItemIdList);
                }

                return collection;
            }
            finally
            {
                // Refresh handled internally
                _iLibraryMonitor.ReportFileSystemChangeComplete(path, false);
            }
        }

        private Folder GetParentFolder(Guid? parentId)
        {
            if (parentId.HasValue)
            {
                if (parentId.Value == Guid.Empty)
                {
                    throw new ArgumentNullException("parentId");
                }

                var folder = _libraryManager.GetItemById(parentId.Value) as Folder;

                // Find an actual physical folder
                if (folder is CollectionFolder)
                {
                    var child = _libraryManager.RootFolder.Children.OfType<Folder>()
                        .FirstOrDefault(i => folder.PhysicalLocations.Contains(i.Path, StringComparer.OrdinalIgnoreCase));

                    if (child != null)
                    {
                        return child;
                    }
                }
            }

            return _libraryManager.RootFolder.Children.OfType<ManualCollectionsFolder>().FirstOrDefault() ??
                _libraryManager.RootFolder.GetHiddenChildren().OfType<ManualCollectionsFolder>().FirstOrDefault();
        }

        public async Task AddToCollection(Guid collectionId, IEnumerable<Guid> ids)
        {
            var collection = _libraryManager.GetItemById(collectionId) as BoxSet;

            if (collection == null)
            {
                throw new ArgumentException("No collection exists with the supplied Id");
            }

            var list = new List<LinkedChild>();
            var currentLinkedChildren = collection.GetLinkedChildren().ToList();

            foreach (var itemId in ids)
            {
                var item = _libraryManager.GetItemById(itemId);

                if (item == null)
                {
                    throw new ArgumentException("No item exists with the supplied Id");
                }

                if (currentLinkedChildren.Any(i => i.Id == itemId))
                {
                    throw new ArgumentException("Item already exists in collection");
                }

                list.Add(new LinkedChild
                {
                    ItemName = item.Name,
                    ItemYear = item.ProductionYear,
                    ItemType = item.GetType().Name,
                    Type = LinkedChildType.Manual
                });

                var supportsGrouping = item as ISupportsBoxSetGrouping;

                if (supportsGrouping != null)
                {
                    var boxsetIdList = supportsGrouping.BoxSetIdList.ToList();
                    if (!boxsetIdList.Contains(collectionId))
                    {
                        boxsetIdList.Add(collectionId);
                    }
                    supportsGrouping.BoxSetIdList = boxsetIdList;
                }
            }

            collection.LinkedChildren.AddRange(list);

            await collection.UpdateToRepository(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

            await collection.RefreshMetadata(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task RemoveFromCollection(Guid collectionId, IEnumerable<Guid> itemIds)
        {
            var collection = _libraryManager.GetItemById(collectionId) as BoxSet;

            if (collection == null)
            {
                throw new ArgumentException("No collection exists with the supplied Id");
            }

            var list = new List<LinkedChild>();

            foreach (var itemId in itemIds)
            {
                var child = collection.LinkedChildren.FirstOrDefault(i => i.ItemId.HasValue && i.ItemId.Value == itemId);

                if (child == null)
                {
                    throw new ArgumentException("No collection title exists with the supplied Id");
                }

                list.Add(child);

                var childItem = _libraryManager.GetItemById(itemId);
                var supportsGrouping = childItem as ISupportsBoxSetGrouping;

                if (supportsGrouping != null)
                {
                    var boxsetIdList = supportsGrouping.BoxSetIdList.ToList();
                    boxsetIdList.Remove(collectionId);
                    supportsGrouping.BoxSetIdList = boxsetIdList;
                }
            }

            var shortcutFiles = Directory
                .EnumerateFiles(collection.Path, "*", SearchOption.TopDirectoryOnly)
                .Where(i => _fileSystem.IsShortcut(i))
                .ToList();

            var shortcutFilesToDelete = list.Where(child => !string.IsNullOrWhiteSpace(child.Path) && child.Type == LinkedChildType.Shortcut)
                .Select(child => shortcutFiles.FirstOrDefault(i => string.Equals(child.Path, _fileSystem.ResolveShortcut(i), StringComparison.OrdinalIgnoreCase)))
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            foreach (var file in shortcutFilesToDelete)
            {
                _iLibraryMonitor.ReportFileSystemChangeBeginning(file);
            }

            try
            {
                foreach (var file in shortcutFilesToDelete)
                {
                    File.Delete(file);
                }
                
                foreach (var child in list)
                {
                    collection.LinkedChildren.Remove(child);
                }
            }
            finally
            {
                foreach (var file in shortcutFilesToDelete)
                {
                    _iLibraryMonitor.ReportFileSystemChangeComplete(file, false);
                }
            }

            await collection.UpdateToRepository(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

            await collection.RefreshMetadata(CancellationToken.None).ConfigureAwait(false);
        }

        public IEnumerable<BaseItem> CollapseItemsWithinBoxSets(IEnumerable<BaseItem> items, User user)
        {
            var itemsToCollapse = new List<ISupportsBoxSetGrouping>();
            var boxsets = new List<BaseItem>();

            var list = items.ToList();

            foreach (var item in list.OfType<ISupportsBoxSetGrouping>())
            {
                var currentBoxSets = item.BoxSetIdList
                    .Select(i => _libraryManager.GetItemById(i))
                    .Where(i => i != null && i.IsVisible(user))
                    .ToList();

                if (currentBoxSets.Count > 0)
                {
                    itemsToCollapse.Add(item);
                    boxsets.AddRange(currentBoxSets);
                }
            }

            return list
                .Except(itemsToCollapse.Cast<BaseItem>())
                .Concat(boxsets)
                .DistinctBy(i => i.Id);
        }
    }
}
