Breaking Changes
----------------

Version 1.9.2.X
===============

You need to add a new configuration setting to your `.cscfg` file:

	<Setting name="SyncEnabled" value="true" />

This is to allow for the ability to selectively disable syncing while the role is running. You might want to do this if there is a problem with Azure Storage or you want to rotate your keys etc.

You also need to update your `web.config` and `app.config` files in your web project to redirect `Microsoft.WindowsAzure.Diagnostics` to 1.8.0.0.

You probably also should upgrade to the Azure 1.8 SDK (it's possible it will still work with 1.7, but not guaranteed). It's worth upgrading to 1.8 because the deployment time is significantly decreased.
