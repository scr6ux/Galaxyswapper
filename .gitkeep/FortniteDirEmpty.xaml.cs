﻿using Galaxy_Swapper_v2.Workspace.Properties;
using Galaxy_Swapper_v2.Workspace.Utilities;
using Serilog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Galaxy_Swapper_v2.Workspace.Usercontrols.Overlays
{
    public partial class FortniteDirEmpty : UserControl
    {
        private BackgroundWorker DetectWorker;
        private string[] UsableProcesses = { "FortniteClient-Win64-Shipping_BE", "FortniteClient-Win64-Shipping_EAC", "FortniteClient-Win64-Shipping_EAC_EOS" };
        private bool EndWorker = false;
        public FortniteDirEmpty() => InitializeComponent();

        private void Close_Click(object sender, MouseButtonEventArgs e)
        {
            EndWorker = true;
            Memory.MainView.RemoveOverlay();
        }
        private void FortniteDirEmpty_Loaded(object sender, RoutedEventArgs e)
        {
            Header.Text = Languages.Read(Languages.Type.View, "FortniteDirEmpty", "Header");
            Description.Text = Languages.Read(Languages.Type.View, "FortniteDirEmpty", "Description");

            DetectWorker = new BackgroundWorker();
            DetectWorker.DoWork += DetectWorker_DoWork;
            DetectWorker.RunWorkerCompleted += DetectWorker_Completed;
            DetectWorker.RunWorkerAsync();
        }

        private async void DetectWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                if (EndWorker)
                    return;

                foreach (string processName in UsableProcesses)
                {
                    Process[] usableProcess = Process.GetProcessesByName("FortniteClient-Win64-Shipping_EAC_EOS");

                    if (usableProcess.Length != 0)
                    {
                        string path = usableProcess[0].MainModule.FileName;

                        Log.Information($"Detected {processName} ({usableProcess[0].Id}) with path: {path}");

                        if (path.Contains("\\FortniteGame"))
                            path = path.Split("\\FortniteGame").First();

                        if (!Directory.Exists($"{path}\\FortniteGame\\Content\\Paks"))
                        {
                            Message.DisplaySTA(Languages.Read(Languages.Type.Header, "Error"), string.Format(Languages.Read(Languages.Type.Message, "DetectedInvalidPath"), path), MessageBoxButton.OK, discord: true);
                            return;
                        }

                        Settings.Edit(Settings.Type.Installtion, path);
                        Log.Information($"Set new path to: {path}");

                        EpicGamesLauncher.Close();
                        Message.DisplaySTA(Languages.Read(Languages.Type.Header, "Info"), string.Format(Languages.Read(Languages.Type.Message, "DetectedNewPath"), path), MessageBoxButton.OK);
                        return;
                    }
                }

                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        private void DetectWorker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            Memory.SettingsView.SettingsView_Loaded(null!, null!);
            Close_Click(null!, null!);
        }
    }
}
