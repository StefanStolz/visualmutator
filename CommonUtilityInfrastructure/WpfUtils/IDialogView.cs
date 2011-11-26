﻿

namespace CommonUtilityInfrastructure.WpfUtils
{

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Windows;

    public interface IDialogView : IView
    {
        bool? ShowDialog();

        void Close();

        Window Owner { get; set; }

    
    }
}
