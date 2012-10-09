# Azure Web Farm #

This is a refresh of the [Accelerator for Web Roles project](https://github.com/microsoft-dpe/wa-accelerator-webroles). The objective of this project is to allow people to continue using the goodness of Accelerator for Web Roles, but with an increasingly production ready and tested code base and an easier upgrade and setup pathway.

## About
The Azure Web Farm allows you to use a Web Role and deploy multiple web sites via MsDeploy to it.

## When Should I Use This? ##
If you [aren't able to use Azure Web Sites](http://robdmoore.id.au/blog/2012/06/09/windows-azure-web-sites-vs-web-roles/), but you don't want a slow and frustrating deployment option or to be locked into deploying only one web site on your roles then this is the project for you.

## Documentation ##

### Installation ###
1. Create a new MVC4 website in Visual Studio using the blank template and delete the Global.asax.cs, Global.asax, Web.config and Views/Web.config files.
2. Install-Package AzureWebFarm
3. (optional) use ReSharper (or similar) to change the namespaces to match your assembly namespace
4. Create a cloud project with the website as a web role
5. Look in the packages/AzureWebFarm/tools/ExampleConfigs folder to see example values to put in the .csdef and .cscfg files for it to work

### Existing Documentation ###
Note this will change over time as the distinguishing features of AzureWebFarm emerge, but for now I'll link to the Accelerator for Web Roles documentation since it's essentially still accurate.
* [Home](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki)
* [Setup and Configuration](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki/Setup-and-Configuration)
* [Deploying](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki/Deploying)
* [Changing Portal Credentials](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki/portal-credentials)
* [Modifying Service Endpoints](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki/service-endpoints)
* [Known Issues](https://github.com/microsoft-dpe/wa-accelerator-webroles/wiki/known-issues)

## Contributions ##
If you would like to contribute to this project then feel free to communicate with myself via Twitter [@robdmoore](http://twitter.com/robdmoore) or alternatively send a pull request.

## Roadmap ##
* Manage setup and maintenance of application via NuGet package
* Provide unit test coverage across most of the code
* Remove the ability to manage via frontend and instead require pre- and/or post-sync MsDeploy commands to manage IIS
* Add the concept of a version of the site rather than scanning for last modified date across the site files
* Add status reporting and a dashboard for all roles in operation and the version they currently have
* If possible, upload the Web Deploy log when syncing a package so it can be inspected via the dashboard
* Investigate putting Kudu in
* Update the WPI exe in Startup and ensure the Web Deploy packages being used are the latest
