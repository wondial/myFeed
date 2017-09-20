﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using myFeed.Services.Abstractions;
using myFeed.Views.Uwp.Views;

namespace myFeed.Views.Uwp.Services
{
    public class UwpNavigationService : INavigationService
    {
        private readonly SystemNavigationManager _systemNavigationManager;
        public static readonly IReadOnlyDictionary<ViewKey, Type> Pages = new Dictionary<ViewKey, Type>
        {
            {ViewKey.SettingsView, typeof(SettingsView)},
            {ViewKey.SourcesView, typeof(SourcesView)},
            {ViewKey.ArticleView, typeof(ArticleView)},
            {ViewKey.SearchView, typeof(SearchView)},
            {ViewKey.FaveView, typeof(FaveView)},
            {ViewKey.FeedView, typeof(FeedView)},
        };

        public UwpNavigationService()
        {
            _systemNavigationManager = SystemNavigationManager.GetForCurrentView();
            _systemNavigationManager.BackRequested += NavigateBack;
            UpdateBackButtonVisibility();
        }

        public Task Navigate(ViewKey viewKey) => Navigate(viewKey, null);

        public Task Navigate(ViewKey viewKey, object parameter)
        {
            switch (viewKey)
            {
                case ViewKey.FeedView:
                case ViewKey.FaveView:
                case ViewKey.SearchView:
                case ViewKey.SourcesView:
                case ViewKey.SettingsView:
                    var splitViewFrame = GetFrame(Window.Current.Content, 0);
                    splitViewFrame?.Navigate(Pages[viewKey], parameter);
                    UpdateBackButtonVisibility();
                    break;
                case ViewKey.ArticleView:
                    var articleFrame = GetFrame(Window.Current.Content, 1);
                    articleFrame?.Navigate(Pages[viewKey], parameter);
                    UpdateBackButtonVisibility();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(viewKey), viewKey, null);
            }
            return Task.CompletedTask;
        }

        private void NavigateBack(object sender, BackRequestedEventArgs e)
        {
            var articleFrame = GetFrame(Window.Current.Content, 1);
            if (articleFrame != null && articleFrame.CanGoBack)
            {
                articleFrame.GoBack();
                UpdateBackButtonVisibility();
                return;
            }
            var splitViewFrame = GetFrame(Window.Current.Content, 0);
            if (splitViewFrame.CanGoBack) splitViewFrame.GoBack();
            UpdateBackButtonVisibility();
        }

        private void UpdateBackButtonVisibility() =>
            _systemNavigationManager.AppViewBackButtonVisibility =
                GetFrame(Window.Current.Content, 0)?.CanGoBack == true ||
                GetFrame(Window.Current.Content, 1)?.CanGoBack == true
                    ? AppViewBackButtonVisibility.Visible
                    : AppViewBackButtonVisibility.Collapsed;

        private static Frame GetFrame(DependencyObject root, int depth)
        {
            var childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (var x = 0; x < childrenCount; x++)
            {
                var child = VisualTreeHelper.GetChild(root, x);
                if (child is Frame thisFrame && depth-- == 0) return thisFrame;
                var frame = GetFrame(child, depth);
                if (frame != null) return frame;
            }
            return null;
        }
    }
}
