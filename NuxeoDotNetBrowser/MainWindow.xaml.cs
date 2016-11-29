/*
 * (C) Copyright 2015 Nuxeo SA (http://nuxeo.com/) and others.
 *
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the GNU Lesser General Public License
 * (LGPL) version 2.1 which accompanies this distribution, and is available at
 * http://www.gnu.org/licenses/lgpl-2.1.html
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 * Lesser General Public License for more details.
 *
 * Contributors:
 *     Gabriel Barata <gbarata@nuxeo.com>
 */

using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NuxeoClient;
using NuxeoClient.Adapters;
using NuxeoClient.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Task = System.Threading.Tasks.Task;

namespace NuxeoDotNetBrowser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private WebClient webClient;
        private Client client = null;
        private Document currentDirectory = null;
        private Documents children = null;
        private string tempfolder = System.IO.Path.GetTempPath() + "\\" + AppDomain.CurrentDomain.FriendlyName;

        private ObservableCollection<DocumentViewModel> documentList = null;
        private List<UserViewModel> userList = null;

        private CustomDialog startTaskWithDialog;

        public ICollectionView DocumentList { get; private set; }
        public ICollectionView UserList { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;

            if (!Directory.Exists(tempfolder))
            {
                Directory.CreateDirectory(tempfolder);
            }
        }

        private void StartProgressRing()
        {
            progressRing.IsActive = true;
            DocumentListView.IsEnabled = false;
            pathInput.IsEnabled = false;
            refreshButton.IsEnabled = false;
            backButton.IsEnabled = false;
            createDirButton.IsEnabled = false;
            uploadButton.IsEnabled = false;
        }

        private void StopProgressRing()
        {
            progressRing.IsActive = false;
            DocumentListView.IsEnabled = true;
            pathInput.IsEnabled = true;
            refreshButton.IsEnabled = true;
            backButton.IsEnabled = true;
            createDirButton.IsEnabled = true;
            uploadButton.IsEnabled = true;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartProgressRing();

            documentList = new ObservableCollection<DocumentViewModel>();
            DocumentList = CollectionViewSource.GetDefaultView(documentList);
            DocumentListView.DataContext = DocumentList;

            userList = new List<UserViewModel>();
            UserList = CollectionViewSource.GetDefaultView(userList);

            startTaskWithDialog = (CustomDialog)this.Resources["StartTaskWithDialog"];
            startTaskWithDialog.DataContext = UserList;

            await UpdateServerInfo();
            await Refresh();

            StopProgressRing();
        }

        private async Task DisplayError(string message, Exception exception)
        {
            Exception e = exception;
            while (e.InnerException != null) e = e.InnerException;
            if (e is ServerErrorException)
            {
                ServerErrorException serv = (ServerErrorException)e;
                await this.ShowMessageAsync("NuxeoClient error", message + Environment.NewLine + "Status code:" + serv.StatusCode + Environment.NewLine + e.Message, MessageDialogStyle.Affirmative);
            }
            else
            {
                await this.ShowMessageAsync(message, e.Message, MessageDialogStyle.Affirmative);
            }
        }

        private async System.Threading.Tasks.Task UpdateServerInfo()
        {
            StartProgressRing();
            client = new Client(Properties.Settings.Default.ServerURL,
                                new NuxeoClient.Authorization(Properties.Settings.Default.Username,
                                                              Properties.Settings.Default.Password))
                                                              .AddDefaultSchema("dublincore");

            webClient = new WebClient();
            webClient.Headers[HttpRequestHeader.Authorization] = client.Authorization.GenerateAuthorizationParameter();

            Title = Properties.Settings.Default.ServerURL;

            try
            {
                EntityList<Entity> ents = (EntityList<Entity>)await client.Operation("UserGroup.Suggestion").SetParameter("searchType", "USER_TYPE").Execute();
                userList.Clear();
                foreach (Entity entity in ents)
                {
                    string username = ((UnknowEntity)entity).Json["username"].ToObject<string>();
                    if (username != Properties.Settings.Default.Username)
                    {
                        userList.Add(new UserViewModel(username));
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayError("Could not get user list", ex);
            }
            StopProgressRing();
        }

        private async Task<Document> GetCurrentDir()
        {
            try
            {
                currentDirectory = (Document)await client.DocumentFromPath(pathInput.Text).Get();
            }
            catch (Exception ex)
            {
                await DisplayError("Could not get current directory", ex);
                return null;
            }
            return currentDirectory;
        }

        private async Task<Documents> GetChildren()
        {
            if (currentDirectory == null)
            {
                return null;
            }
            try
            {
                children = (Documents)await currentDirectory.SetAdapter(new ChildrenAdapter()).Get();
            }
            catch (Exception ex)
            {
                await DisplayError("Could not get current directory's children", ex);
                return null;
            }
            return children;
        }

        public async Task<ObservableCollection<DocumentViewModel>> GetThumbnails()
        {
            if (children == null)
                return null;
            documentList.Clear();
            foreach (Document document in children.Entries)
            {
                Document docWithThumbnail = null;
                try
                {
                    docWithThumbnail = (Document)await document.AddContentEnricher("thumbnail").Get();
                }
                catch (Exception ex)
                {
                    await DisplayError("Could not get the tumbnail url of document \"" + document.Path + "\"", ex);
                    continue;
                }

                string thumbnailUrl = docWithThumbnail.ContextParameters["thumbnail"]["url"].ToString();
                BitmapImage thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                if (!string.IsNullOrEmpty(thumbnailUrl))
                {
                    string tempFile = tempfolder + "\\" + System.IO.Path.GetFileNameWithoutExtension(document.Path) + ".jpg";
                    if (!File.Exists(tempFile))
                    {
                        try
                        {
                            webClient.DownloadFile(thumbnailUrl, tempFile);
                        }
                        catch (Exception ex)
                        {
                            await DisplayError("Could not download thumbnal of document \"" + document.Path + "\"", ex);
                            continue;
                        }
                    }
                    Uri resourceFile = new Uri(System.IO.Path.Combine(tempfolder, tempFile));
                    thumbnail.UriSource = resourceFile;
                }
                else
                {
                    thumbnail.UriSource = new Uri("pack://application:,,,/Icons/" + (docWithThumbnail.Type == "Folder" ? "folder.png" : "file.png"));
                }

                thumbnail.EndInit();
                documentList.Add(new DocumentViewModel(document, thumbnail));
            }
            return documentList;
        }

        public async Task UpdateView()
        {
            await GetChildren();
            await GetThumbnails();
        }

        public async Task Refresh()
        {
            await GetCurrentDir();
            await UpdateView();
        }

        private async Task Upload(string[] files, string destination)
        {
            StartProgressRing();
            Uploader uploader = new Uploader(client);
            uploader.AddFiles(files);
            try
            {
                await uploader.UploadFiles();
                await uploader.Operation("FileManager.Import")
                              .SetContext("currentDocument", currentDirectory.Path)
                              .Execute();
            }
            catch (Exception ex)
            {
                await DisplayError("Could not upload files.", ex);
            }
            await UpdateView();
            StopProgressRing();
        }

        private async void UploadFileCommand(object sender, ExecutedRoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = true;
            if (dialog.ShowDialog() == true)
            {
                await Application.Current.Dispatcher.Invoke(() => Upload(dialog.FileNames, pathInput.Text));
            }
            e.Handled = true;
        }

        private async Task OpenDir(Document document)
        {
            if (document != null && document.Facets.Contains("Folderish"))
            {
                StartProgressRing();
                pathInput.Text = document.Path;
                StopProgressRing();
            }
            await Refresh();
        }

        private async Task CreateDir(string title)
        {
            StartProgressRing();
            try
            {
                await currentDirectory.SetAdapter(null).Post(new Document
                {
                    Name = Regex.Replace(title, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled),
                    Type = "Folder",
                    Properties = new NuxeoClient.Wrappers.Properties { { "dc:title", title } }
                });
            }
            catch (Exception ex)
            {
                await DisplayError("Could not create directory \"" + title + "\"", ex);
            }
            await UpdateView();
            StopProgressRing();
        }

        private async Task DeleteDocument(List<Document> documents)
        {
            MessageDialogResult result = await this.ShowMessageAsync("Delete Confirmation", "Do you really want to delete " + string.Join(",", (from document in documents select "\"" + document.Title + "\"")) + "?", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings
            {
                AffirmativeButtonText = "Yes",
                NegativeButtonText = "No"
            });
            if (result == MessageDialogResult.Affirmative)
            {
                StartProgressRing();
                foreach(Document document in documents)
                {
                    try
                    {
                        await document.Delete();
                    }
                    catch (Exception ex)
                    {
                        await DisplayError("Could not delete document \"" + document.Path + "\"", ex);
                    }
                }
                await UpdateView();
                StopProgressRing();
            }
        }

        private async void CreateDirCommand(object sender, ExecutedRoutedEventArgs e)
        {
            string title = await this.ShowInputAsync("Crete a new directory", "Directorty title:");
            if (!string.IsNullOrEmpty(title))
            {
                await CreateDir(title);
            }
            e.Handled = true;
        }

        private async void RefreshCommand(object sender, ExecutedRoutedEventArgs e)
        {
            StartProgressRing();
            await Refresh();
            StopProgressRing();
            e.Handled = true;
        }

        private async void BackCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (currentDirectory.ParentRef != currentDirectory.Path)
            {
                StartProgressRing();
                try
                {
                    currentDirectory = (Document)await client.DocumentFromUid(currentDirectory.ParentRef).Get();
                    pathInput.Text = currentDirectory.Path;
                }
                catch (Exception ex)
                {
                    await DisplayError("Could not get parent of current directory.", ex);
                }
                await UpdateView();
                StopProgressRing();
            }
            e.Handled = true;
        }

        private async void UpdateServerCommand(object sender, ExecutedRoutedEventArgs e)
        {
            string url = await this.ShowInputAsync("Connection details", "Server address:", new MetroDialogSettings
            {
                DefaultText = Properties.Settings.Default.ServerURL,
                AffirmativeButtonText = "Save",
                NegativeButtonText = "Cancel"
            });
            if (!string.IsNullOrEmpty(url))
            {
                if (url != Properties.Settings.Default.ServerURL)
                {
                    StartProgressRing();
                    Properties.Settings.Default.ServerURL = url;
                    Properties.Settings.Default.Save();
                    await UpdateServerInfo();
                    await Refresh();
                    StopProgressRing();
                }
            }
            e.Handled = true;
        }

        private async void UpdateAuthCommand(object sender, ExecutedRoutedEventArgs e)
        {
            LoginDialogData loginData = await this.ShowLoginAsync("Authentication details", "Username & password:", new LoginDialogSettings
            {
                UsernameWatermark = "username",
                PasswordWatermark = "password",
                AffirmativeButtonText = "Save",
                NegativeButtonVisibility = Visibility.Visible,
                NegativeButtonText = "Cancel",
                InitialUsername = Properties.Settings.Default.Username,
                InitialPassword = Properties.Settings.Default.Password,
                EnablePasswordPreview = true
            });
            if (loginData != null)
            {
                if (loginData.Username != Properties.Settings.Default.Username ||
                    loginData.Password != Properties.Settings.Default.Password)
                {
                    StartProgressRing();
                    Properties.Settings.Default.Username = loginData.Username;
                    Properties.Settings.Default.Password = loginData.Password;
                    Properties.Settings.Default.Save();
                    await UpdateServerInfo();
                    await Refresh();
                    StopProgressRing();
                }
            }
            e.Handled = true;
        }

        private async void DeleteDocument_Click(object sender, RoutedEventArgs e)
        {
            IList<DocumentViewModel> views = DocumentListView.SelectedItems.Cast<DocumentViewModel>().ToList();
            if (views != null)
            {
                await DeleteDocument((from view in views select view.Document).ToList());
            }
            e.Handled = true;
        }

        private async void OpenDir_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentListView.SelectedItem != null)
            {
                Document document = ((DocumentViewModel)DocumentListView.SelectedItem).Document;
                await OpenDir(document);
            }
            e.Handled = true;
        }

        private async void pathInput_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await Refresh();
            }
            e.Handled = true;
        }

        private async void DocumentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DocumentListView.SelectedItem != null)
            {
                Document document = ((DocumentViewModel)DocumentListView.SelectedItem).Document;
                await OpenDir(document);
            }
            e.Handled = true;
        }

        private async void DocumentListView_KeyUp(object sender, KeyEventArgs e)
        {
            if (DocumentListView.SelectedItem != null)
            {
                if (e.Key == Key.Enter)
                {
                    Document document = ((DocumentViewModel)DocumentListView.SelectedItem).Document;
                    await OpenDir(document);
                }
                else if (e.Key == Key.Delete)
                {
                    IList<DocumentViewModel> views = DocumentListView.SelectedItems.Cast< DocumentViewModel>().ToList();
                    await DeleteDocument((from view in views select view.Document).ToList());
                }
            }
            e.Handled = true;
        }

        private async void StartParallelReview_Click(object sender, RoutedEventArgs e)
        {
            await this.ShowMetroDialogAsync(startTaskWithDialog);
            e.Handled = true;
        }

        private async void StartTask_Click(object sender, RoutedEventArgs e)
        {
            await this.HideMetroDialogAsync(startTaskWithDialog);

            StartProgressRing();

            List<string> selectedUsers = (from user in userList where user.IsChecked == true select user.Username).ToList();
            userList.ForEach(user => user.IsChecked = false);

            JArray participants = new JArray(),
                   assigness = new JArray();

            participants.Add("user:" + Properties.Settings.Default.Username);
            assigness.Add("user:" + Properties.Settings.Default.Username);

            foreach (string user in selectedUsers)
            {
                participants.Add("user:" + user);
                assigness.Add("user:" + user);
            }

            Document document = ((DocumentViewModel)DocumentListView.SelectedItem).Document;

            try
            {
                Document node = (Document)await document.SetAdapter(new WorkflowAdapter()).Post(new Workflow
                {
                    WorkflowModelName = "ParallelDocumentReview",
                    AttachedDocumentIds = new JArray() { document.Uid }
                });
                Tasks tasks = (Tasks)await document.Get("/" + node.Uid + "/task");
                NuxeoClient.Wrappers.Task task = tasks.Entries[0];

                NuxeoClient.Wrappers.Task completedTask = (NuxeoClient.Wrappers.Task)await document.SetAdapter(new TaskAdapter()).Put(new NuxeoClient.Wrappers.Task
                {
                    Id = task.Id,
                    Comment = "a comment",
                    Variables = new NuxeoClient.Wrappers.Properties
                {
                    { "end_date", DateTime.Now.Date.ToString("yyyy-MM-dd") },
                    { "participants", participants },
                    { "assignees", assigness }
                }
                }, "/" + task.Id + "/start_review");
            }
            catch (Exception ex)
            {
                await DisplayError("Could not start parallel review task.", ex);
            }
            await UpdateView();
            StopProgressRing();
        }

        private void CancelTask_Click(object sender, RoutedEventArgs e)
        {
            this.HideMetroDialogAsync(startTaskWithDialog);
        }

        private void SearchParticipants_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            string[] query = null;
            if (textBox.Text.Trim() != string.Empty)
            {
                query = textBox.Text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            QueryUsers(query);
        }

        private void QueryUsers(string[] query)
        {
            if (query != null)
            {
                UserList.Filter = delegate (object user)
                {
                    foreach (string q in query)
                        if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(((UserViewModel)user).Username, q, CompareOptions.IgnoreCase) < 0)
                            return false;
                    return true;
                };
            }
            else
            {
                UserList.Filter = null;
            }
        }
    }
}