Breaking Changes
----------------

Version 1.9.2.X
===============

You need to add a new configuration setting to your `.cscfg` file:

    <Setting name="SyncEnabled" value="true" />

This is to allow for the ability to selectively disable syncing while the role is running. You might want to do this if there is a problem with Azure Storage or you want to rotate your keys etc.

You also need to change the Startup section of your `.csdef` file to:

    <Startup>
      <Task commandLine="Startup\ConfigureIIS.cmd" executionContext="elevated" taskType="simple">
        <Environment>
          <Variable name="EMULATED">
            <RoleInstanceValue xpath="/RoleEnvironment/Deployment/@emulated" />
          </Variable>
        </Environment>
      </Task>
    </Startup>

You also need to update your `web.config` and `app.config` files in your web project to redirect `Microsoft.WindowsAzure.Diagnostics` to 1.8.0.0.

You also need to upgrade to the Azure 1.8 SDK - this gives you a faster deployment time and support for Windows Server 2012.

You also need to upgrade to Windows Server 2012; you can do this by simply changing the `osversion` attribute to the value `3` in your `.cscfg` file and read the following [http://blogs.msdn.com/b/avkashchauhan/archive/2012/10/29/using-windows-server-2012-os-with-windows-azure-cloud-services-and-net-4-5.aspx](http://blogs.msdn.com/b/avkashchauhan/archive/2012/10/29/using-windows-server-2012-os-with-windows-azure-cloud-services-and-net-4-5.aspx).

todo: document the other breaking changes from the startup tasks change
