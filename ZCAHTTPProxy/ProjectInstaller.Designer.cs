using System.Text;
using System.Reflection;

namespace ZCAHTTPProxy
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnBeforeInstall(System.Collections.IDictionary savedState)
        {
#if true
            StringBuilder path = new StringBuilder(Context.Parameters["assemblypath"]);
            if (path[0] != '"')
            {
                path.Insert(0, '"');
                path.Append('"');
            }
            path.Append(" /service-run");
            Context.Parameters["assemblypath"] = path.ToString();
#else
            Context.Parameters["assemblypath"] += "\" \"/service-run";
#endif // false
            base.OnBeforeInstall(savedState);
        }

        protected override void OnBeforeUninstall(System.Collections.IDictionary savedState)
        {
#if true
            StringBuilder path = new StringBuilder(Context.Parameters["assemblypath"]);
            if (path[0] != '"')
            {
                path.Insert(0, '"');
                path.Append('"');
            }
            path.Append(" /service-run");
            Context.Parameters["assemblypath"] = path.ToString();
#else
            Context.Parameters["assemblypath"] += "\" \"/service-run";
#endif // false
            base.OnBeforeUninstall(savedState);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.serviceProcessInstaller1 = new System.ServiceProcess.ServiceProcessInstaller();
            this.serviceInstaller1 = new System.ServiceProcess.ServiceInstaller();
            // 
            // serviceProcessInstaller1
            // 
            this.serviceProcessInstaller1.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.serviceProcessInstaller1.Password = null;
            this.serviceProcessInstaller1.Username = null;
            // 
            // serviceInstaller1
            // 
            this.serviceInstaller1.Description = "Multicast UDP to HTTP proxy with Interzet CA descrambler.";
            //this.serviceInstaller1.DisplayName = "ZCAHTTPProxy";
            //this.serviceInstaller1.ServiceName = "ZCAHTTPProxy";

            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();
            this.serviceInstaller1.DisplayName = assemName.Name;
            this.serviceInstaller1.ServiceName = assemName.Name;
            this.serviceInstaller1.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.serviceProcessInstaller1,
            this.serviceInstaller1});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller serviceProcessInstaller1;
        private System.ServiceProcess.ServiceInstaller serviceInstaller1;
    }
}