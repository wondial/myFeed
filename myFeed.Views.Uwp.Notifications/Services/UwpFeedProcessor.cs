﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using myFeed.Entities.Local;
using myFeed.Repositories.Abstractions;
using myFeed.Services.Abstractions;

namespace myFeed.Views.Uwp.Notifications.Services
{
    internal sealed class UwpFeedProcessor
    {
        private readonly ISourcesRepository _sourcesRepository;
        private readonly IFeedStoreService _feedStoreService;
        private readonly ISettingsService _settingsService;

        public UwpFeedProcessor(
            IFeedStoreService feedStoreService, 
            ISourcesRepository sourcesRepository,
            ISettingsService settingsService)
        {
            _sourcesRepository = sourcesRepository;
            _feedStoreService = feedStoreService;
            _settingsService = settingsService;
        }

        public async Task ProcessFeeds()
        {
            // Fetch feed for all existing sources where user allowed notifications.
            var categories = await _sourcesRepository.GetAllAsync().ConfigureAwait(false);
            var sources = categories.SelectMany(i => i.Sources).Where(i => i.Notify);
            var feed = await _feedStoreService.GetAsync(sources).ConfigureAwait(false);

            // Get preferences settings.
            var recentFetchDateTime = await _settingsService.Get<string>("LastFetched");
            var recentFetch = DateTime.Parse(recentFetchDateTime, CultureInfo.InvariantCulture);
            var needBanners = await _settingsService.Get<bool>("NeedBanners");
            var needImages = await _settingsService.Get<bool>("LoadImages");

            // Get recent items.
            var recentItems = feed.Item2
                .Where(i => i.PublishedDate > recentFetch)
                .OrderByDescending(i => i.PublishedDate)
                .Take(15).Reverse().ToList();
            
            // Show notifications for revant articles.
            foreach (var entity in recentItems) 
                SendToastNotification(entity, needBanners, needImages);
            SendTileNotifications(recentItems);

            // Update last fetch date.
            await _settingsService.Set("LastFetched", DateTime.Now.ToString(CultureInfo.InvariantCulture));
        }

        private static void SendTileNotifications(IEnumerable<ArticleEntity> receivedArticles)
        {
            var updateManager = TileUpdateManager.CreateTileUpdaterForApplication();
            updateManager.EnableNotificationQueue(true);
            updateManager.Clear();

            foreach (var article in receivedArticles.Take(5))
            {
                var template = GetTileTemplate(article.FeedTitle, article.Title);
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(template);
                var notification = new TileNotification(xmlDocument);
                updateManager.Update(notification);
            }
        }

        private static void SendToastNotification(ArticleEntity article, bool needBanners, bool needImages)
        {
            var identifier = article.Id.ToString();
            var uri = needImages ? article.ImageUri : string.Empty;
            var template = GetNotificationTemplate(article.Title, 
                article.FeedTitle, uri, identifier);

            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(template);
            var notification = new ToastNotification(xmlDocument) { SuppressPopup = !needBanners };
            ToastNotificationManager.CreateToastNotifier().Show(notification);
        }

        private static string GetTileTemplate(string title, string message) => $@"
            <tile>
                <visual>
                    <binding template='TileMedium'>
                        <text hint-style='captionSubtle'>{title}</text>
                        <text hint-style='base' hint-wrap='true'>{message}</text>
                    </binding>
                    <binding template='TileWide'>
                        <text hint-style='captionSubtle'>{title}</text>
                        <text hint-style='base' hint-wrap='true'>{message}</text>
                    </binding>
                    <binding template='TileLarge'>
                        <text hint-style='captionSubtle'>{title}</text>
                        <text hint-style='base' hint-wrap='true'>{message}</text>
                    </binding>
                </visual>
            </tile>";

        private static string GetNotificationTemplate(string title, string message, string imageUri, string id) => $@"
            <toast launch='{id}'>
                <visual>
                    <binding template='ToastGeneric'>
                        <text>{title}</text>
                        <text>{message}</text>
                        {(Uri.IsWellFormedUriString(imageUri, UriKind.Absolute) 
                          ? $@"<image src='{imageUri}' placement='appLogoOverride' hint-crop='circle'/>"
                          : string.Empty)}
                    </binding>
                </visual>
                <actions>
                    <action activationType='foreground' content='Read more' arguments='{id}'/>
                </actions>
            </toast>";
    }
}
