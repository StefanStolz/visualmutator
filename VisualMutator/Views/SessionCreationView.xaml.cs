﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VisualMutator.Views
{
    using CommonUtilityInfrastructure.WpfUtils;

    public interface ISessionCreationView : IDialogView
    {
        bool? SetOwnerAndShowDialog();
    }


    public partial class SessionCreationView : Window, ISessionCreationView
    {
        private readonly IOwnerWindowProvider _windowProvider;

        public SessionCreationView(IOwnerWindowProvider windowProvider)
        {
            _windowProvider = windowProvider;
            InitializeComponent();
        }

        public bool? SetOwnerAndShowDialog()
        {
            _windowProvider.SetOwner(this);
            return ShowDialog();
        }
    }
}