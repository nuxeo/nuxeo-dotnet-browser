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

using NuxeoClient.Wrappers;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace NuxeoDotNetBrowser
{
    public class DocumentViewModel : INotifyPropertyChanged
    {
        private Document document;

        public Document Document
        {
            get
            {
                return document;
            }
            set
            {
                document = value;
                NotifyPropertyChanged("Document");
            }
        }

        private BitmapImage thumbnail;

        public BitmapImage Thumbnail
        {
            get
            {
                return thumbnail;
            }
            set
            {
                thumbnail = value;
                NotifyPropertyChanged("Thumbnail");
            }
        }

        private bool isFolder;

        public bool IsFolder
        {
            get
            {
                return isFolder;
            }
            set
            {
                isFolder = value;
                NotifyPropertyChanged("IsFolder");
            }
        }

        private bool isFile;

        public bool IsFile
        {
            get
            {
                return isFile;
            }
            set
            {
                isFile = value;
                NotifyPropertyChanged("IsFile");
            }
        }

        private string title;

        public string Title
        {
            get
            {
                return title;
            }
            set
            {
                title = value;
                NotifyPropertyChanged("Title");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public DocumentViewModel(Document document, BitmapImage thumbnail)
        {
            Document = document;
            Thumbnail = thumbnail;
            IsFolder = document.Type == "Folder";
            IsFile = document.Type != "Folder" && document.Type != "Domain";
            Title = document.Properties["dc:title"]?.ToObject<string>() ?? document.Title;
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}