﻿using Microsoft.Office.Interop.Word;
using System.Windows.Forms;

namespace Chem4Word.Navigator
{
    public partial class NavigatorHost : UserControl
    {
        private Document _activeDoc;

        public NavigatorHost()
        {
            InitializeComponent();
        }

        public NavigatorHost(Microsoft.Office.Interop.Word.Application app, Document doc) : this()
        {
            ActiveApp = app;
            ActiveDoc = doc;
        }

        public Document ActiveDoc
        {
            get { return _activeDoc; }

            set
            {
                _activeDoc = value;
                navigatorView1.ActiveDocument = value;
            }
        }

        private Microsoft.Office.Interop.Word.Application _activeApp;

        public Microsoft.Office.Interop.Word.Application ActiveApp
        {
            get { return _activeApp; }
            set
            {
                if (_activeApp != null)
                {
                    //_activeApp.
                }

                _activeApp = value;
                navigatorView1.ActiveApplication = value;
            }
        }
    }
}