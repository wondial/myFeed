﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using myFeed.Services.Platform;

namespace myFeed.Views.Uwp.Services
{
    public sealed class UwpPlatformService : IPlatformService
    {
        private static readonly Dictionary<string, ElementTheme> Themes = new Dictionary<string, ElementTheme>
        {
            {"dark", ElementTheme.Dark},
            {"light", ElementTheme.Light},
            {"default", ElementTheme.Default}
        };

        public async Task LaunchUri(Uri uri) => await Launcher.LaunchUriAsync(uri);

        public Task Share(string content)
        {
            var dataTransferManager = DataTransferManager.GetForCurrentView();
            dataTransferManager.DataRequested += (sender, args) =>
            {
                var request = args.Request;
                request.Data.SetText(content);
                request.Data.Properties.Title = "myFeed";
            };
            DataTransferManager.ShowShareUI();
            return Task.CompletedTask;
        }

        public Task CopyTextToClipboard(string text)
        {
            var dataPackage = new DataPackage {RequestedOperation = DataPackageOperation.Copy};
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            return Task.CompletedTask;
        }

        public async Task ResetApp()
        {
            var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
            foreach (var file in files) await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            Application.Current.Exit();
        }
        
        public async Task RegisterBackgroundTask(int freq)
        {
            var backgroundAccessStatus = await BackgroundExecutionManager.RequestAccessAsync();
            foreach (var task in BackgroundTaskRegistration.AllTasks)
                if (task.Value.Name == "myFeedNotify")
                    task.Value.Unregister(true);

            if (freq == 0) return;
            if (freq < 30) freq = 30;
            var builder = new BackgroundTaskBuilder {Name="myFeedNotify"};
            builder.SetTrigger(new TimeTrigger((uint)freq, false));
            builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
            builder.TaskEntryPoint = "myFeed.Views.Uwp.Notifications.Runner";
            builder.Register();
        }

        public Task RegisterTheme(string theme)
        {
            var contentElement = (Frame)Window.Current.Content;
            contentElement.RequestedTheme = Themes[theme];
            return Task.CompletedTask;
        }
    }
}