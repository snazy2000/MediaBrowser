﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dlna;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MediaBrowser.Dlna.PlayTo
{
    public class PlaylistItemFactory
    {
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        public PlaylistItem Create(Photo item, DeviceProfile profile)
        {
            var playlistItem = new PlaylistItem
            {
                StreamInfo = new StreamInfo
                {
                    ItemId = item.Id.ToString("N"),
                    MediaType = DlnaProfileType.Photo,
                },

                Profile = profile
            };

            var directPlay = profile.DirectPlayProfiles
                .FirstOrDefault(i => i.Type == DlnaProfileType.Photo && IsSupported(i, item));

            if (directPlay != null)
            {
                playlistItem.StreamInfo.IsDirectStream = true;
                playlistItem.StreamInfo.Container = Path.GetExtension(item.Path);

                return playlistItem;
            }

            var transcodingProfile = profile.TranscodingProfiles
                .FirstOrDefault(i => i.Type == DlnaProfileType.Photo);

            if (transcodingProfile != null)
            {
                playlistItem.StreamInfo.IsDirectStream = true;
                playlistItem.StreamInfo.Container = "." + transcodingProfile.Container.TrimStart('.');
            }

            return playlistItem;
        }

        private bool IsSupported(DirectPlayProfile profile, Photo item)
        {
            var mediaPath = item.Path;

            if (profile.Container.Length > 0)
            {
                // Check container type
                var mediaContainer = Path.GetExtension(mediaPath);
                if (!profile.GetContainers().Any(i => string.Equals("." + i.TrimStart('.'), mediaContainer, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
