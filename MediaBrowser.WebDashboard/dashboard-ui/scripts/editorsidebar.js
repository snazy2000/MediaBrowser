﻿(function ($, document, window) {

    function getNode(item, folderState) {

        var state = item.IsFolder ? folderState : '';

        var htmlName = getNodeInnerHtml(item);

        var rel = item.IsFolder ? 'folder' : 'default';

        return { attr: { id: item.Id, rel: rel, itemtype: item.Type }, data: htmlName, state: state };
    }
    
    function getNodeInnerHtml(item) {
        
        var name = item.Name;

        // Channel number
        if (item.Number) {
            name = item.Number + " - " + name;
        }
        if (item.IndexNumber != null && item.Type != "Season") {
            name = item.IndexNumber + " - " + name;
        }

        var cssClass = "editorNode";

        if (item.LocationType == "Offline") {
            cssClass += " offlineEditorNode";
        }

        var htmlName = "<div class='" + cssClass + "'>";

        if (item.LockData) {
            htmlName += '<img src="css/images/editor/lock.png" />';
        }

        htmlName += name;

        if (!item.LocalTrailerCount && item.Type == "Movie") {
            htmlName += '<img src="css/images/editor/missingtrailer.png" title="Missing local trailer." />';
        }

        if (!item.ImageTags || !item.ImageTags.Primary) {
            htmlName += '<img src="css/images/editor/missingprimaryimage.png" title="Missing primary image." />';
        }

        if (!item.BackdropImageTags || !item.BackdropImageTags.length) {
            if (item.Type !== "Episode" && item.Type !== "Season" && item.MediaType !== "Audio" && item.Type !== "TvChannel" && item.Type !== "MusicAlbum") {
                htmlName += '<img src="css/images/editor/missingbackdrop.png" title="Missing backdrop image." />';
            }
        }

        if (!item.ImageTags || !item.ImageTags.Logo) {
            if (item.Type == "Movie" || item.Type == "Trailer" || item.Type == "Series" || item.Type == "MusicArtist" || item.Type == "BoxSet") {
                htmlName += '<img src="css/images/editor/missinglogo.png" title="Missing logo image." />';
            }
        }

        if (item.Type == "Episode" && item.LocationType == "Virtual") {

            try {
                if (item.PremiereDate && (new Date().getTime() >= parseISO8601Date(item.PremiereDate, { toLocal: true }).getTime())) {
                    htmlName += '<img src="css/images/editor/missing.png" title="Missing episode." />';
                }
            } catch (err) {

            }

        }

        htmlName += "</div>";

        return htmlName;
    }

    function loadChildrenOfRootNode(page, callback, openItems, selectedId) {

        var promise2 = ApiClient.getLiveTvChannels({limit: 0});

        $.when(promise2).done(function (response2) {

            var result = response2;

            var nodes = [];

            nodes.push({ attr: { id: 'MediaFolders', rel: 'folder', itemtype: 'mediafolders' }, data: 'Media Folders', state: 'open' });

            if (result.TotalRecordCount) {
                nodes.push({ attr: { id: 'livetv', rel: 'folder', itemtype: 'livetv' }, data: 'Live TV', state: 'closed' });
            }

            callback(nodes);

            if (selectedId && nodes.filter(function (f) {

                return f.attr.id == selectedId;

            }).length) {

                selectNode(page, selectedId);
            }
        });
    }

    function loadLiveTvChannels(service, openItems, callback) {

        ApiClient.getLiveTvChannels({ ServiceName: service }).done(function (result) {

            var nodes = result.Items.map(function (i) {

                var state = openItems.indexOf(i.Id) == -1 ? 'closed' : 'open';

                return getNode(i, state);

            });

            callback(nodes);

        });

    }

    function loadMediaFolders(service, openItems, callback) {

        $.getJSON(ApiClient.getUrl("Library/MediaFolders")).done(function (result) {

            var nodes = result.Items.map(function (i) {

                var state = openItems.indexOf(i.Id) == -1 ? 'closed' : 'open';

                return getNode(i, state);

            });

            callback(nodes);

        });

    }

    function loadNode(page, node, openItems, selectedId, currentUser, callback) {

        if (node == '-1') {

            loadChildrenOfRootNode(page, callback, openItems, selectedId);
            return;
        }

        var id = node.attr("id");

        var itemtype = node.attr("itemtype");

        if (itemtype == 'livetv') {

            loadLiveTvChannels(id, openItems, callback);
            return;
        }

        if (itemtype == 'mediafolders') {

            loadMediaFolders(id, openItems, callback);
            return;
        }

        var query = {
            ParentId: id,
            Fields: 'Settings'
        };

        if (itemtype != "Season" && itemtype != "Series") {
            query.SortBy = "SortName";
        }

        ApiClient.getItems(Dashboard.getCurrentUserId(), query).done(function (result) {

            var nodes = result.Items.map(function (i) {

                var state = openItems.indexOf(i.Id) == -1 ? 'closed' : 'open';

                return getNode(i, state);

            });

            callback(nodes);

            if (selectedId && result.Items.filter(function (f) {

                return f.Id == selectedId;

            }).length) {

                selectNode(page, selectedId);
            }

        });

    }

    function selectNode(page, id) {

        var elem = $('#' + id, page)[0];

        $.jstree._reference(".libraryTree", page).select_node(elem);

        if (elem) {
            elem.scrollIntoView();
        }

        $(document).scrollTop(0);
    }

    function initializeTree(page, currentUser, openItems, selectedId) {

        $('.libraryTree', page).jstree({

            "plugins": ["themes", "ui", "json_data"],

            data: function (node, callback) {
                loadNode(page, node, openItems, selectedId, currentUser, callback);
            },

            json_data: {

                data: function (node, callback) {
                    loadNode(page, node, openItems, selectedId, currentUser, callback);
                }

            },

            core: { initially_open: [], load_open: true, html_titles: true },
            ui: { initially_select: [] },

            themes: {
                theme: 'mb3',
                url: 'thirdparty/jstree1.0/themes/mb3/style.css?v=' + Dashboard.initialServerVersion
            }

        }).off('select_node.jstree').on('select_node.jstree', function (event, data) {

            var eventData = {
                id: data.rslt.obj.attr("id"),
                itemType: data.rslt.obj.attr("itemtype")
            };

            if (eventData.itemType != 'livetv' && eventData.itemType != 'mediafolders') {
                $(this).trigger('itemclicked', [eventData]);
            }

        });
    }
    
    function updateEditorNode(page, item) {

        var elem = $('#' + item.Id + '>a', page)[0];

        if (elem == null) {
            return;
        }

        $('.editorNode', elem).remove();

        $(elem).append(getNodeInnerHtml(item));
        
        if (item.IsFolder) {

            var tree = jQuery.jstree._reference(".libraryTree");
            var currentNode = tree._get_node(null, false);
            tree.refresh(currentNode);
        }
    }

    $(document).on('itemsaved', ".metadataEditorPage", function (e, item) {

        updateEditorNode(this, item);

    }).on('pagebeforeshow', ".metadataEditorPage", function () {

        window.MetadataEditor = new metadataEditor();

        var page = this;

        Dashboard.getCurrentUser().done(function (user) {

            var id = MetadataEditor.currentItemId;

            if (id) {

                ApiClient.getAncestorItems(id, user.Id).done(function (ancestors) {

                    var ids = ancestors.map(function (i) {
                        return i.Id;
                    });

                    initializeTree(page, user, ids, id);
                });

            } else {
                initializeTree(page, user, []);
            }

        });

    }).on('pagebeforehide', ".metadataEditorPage", function () {

        var page = this;

        $('.libraryTree', page).off('select_node.jstree');

    });

    function metadataEditor() {

        var self = this;

        function ensureInitialValues() {

            if (self.currentItemType || self.currentItemId) {
                return;
            }

            var url = window.location.hash || window.location.toString();

            var id = getParameterByName('id', url);

            if (id) {
                self.currentItemId = id;
                self.currentItemType = null;
            }
        };

        self.getItemPromise = function () {

            var currentItemType = self.currentItemType;
            var currentItemId = self.currentItemId;

            if (currentItemType == "TvChannel") {
                return ApiClient.getLiveTvChannel(currentItemId);
            }

            if (currentItemId) {
                return ApiClient.getItem(Dashboard.getCurrentUserId(), currentItemId);
            }

            return ApiClient.getRootFolder(Dashboard.getCurrentUserId());
        };

        self.getEditQueryString = function (item) {

            var query = "id=" + item.Id;

            var context = getParameterByName('context');

            if (context) {
                query += "&context=" + context;
            }

            return query;
        };

        ensureInitialValues();
    }


})(jQuery, document, window);