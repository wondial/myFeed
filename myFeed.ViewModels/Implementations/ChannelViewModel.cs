﻿using System;
using myFeed.Repositories.Abstractions;
using myFeed.Repositories.Models;
using myFeed.Services.Platform;
using myFeed.ViewModels.Bindables;

namespace myFeed.ViewModels.Implementations
{
    public sealed class ChannelViewModel
    { 
        public ObservableProperty<string> Url { get; }
        public ObservableProperty<string> Name { get; }
        public ObservableProperty<bool> Notify { get; }

        public ObservableCommand DeleteSource { get; }
        public ObservableCommand OpenInBrowser { get; }
        public ObservableCommand CopyLink { get; }

        public ChannelViewModel(
            IPlatformService platformService,
            ICategoriesRepository categoriesRepository,
            ChannelCategoryViewModel parentViewModel,
            Channel channel)
        {
            Url = channel.Uri;
            Notify = channel.Notify;
            Name = new Uri(channel.Uri).Host;
            
            CopyLink = new ObservableCommand(() => platformService.CopyTextToClipboard(channel.Uri));
            OpenInBrowser = new ObservableCommand(async () =>
            {
                if (!Uri.IsWellFormedUriString(channel.Uri, UriKind.Absolute)) return;
                var uri = new Uri(channel.Uri);
                var plainUri = new Uri(string.Format("{0}://{1}", uri.Scheme, uri.Host));
                await platformService.LaunchUri(plainUri);
            });
            DeleteSource = new ObservableCommand(async () =>
            {
                parentViewModel.Items.Remove(this);
                await categoriesRepository.RemoveChannelAsync(
                    parentViewModel.Category.Value, channel);
            });
            Notify.PropertyChanged += async (sender, args) =>
            {
                channel.Notify = Notify.Value;
                await categoriesRepository.UpdateAsync(
                    parentViewModel.Category.Value);
            };
        }
    }
}