# Azure Web Farm #

This is a refresh of the [Accelerator for Web Roles project](https://github.com/microsoft-dpe/wa-accelerator-webroles). The objective of this project is to allow people to continue using the goodness of Accelerator for Web Roles, but with an increasingly production ready and tested code base and an easier upgrade and setup pathway.

## About
The Azure Web Farm allows you to use a Web Role and deploy multiple web sites via MsDeploy to it. It also allows you to run lightweight console applications in the background if they are deployed alongside your websites according to a particular convention.

## When Should I Use This? ##
If you [aren't able to use Azure Web Sites](http://robdmoore.id.au/blog/2012/06/09/windows-azure-web-sites-vs-web-roles/), but you don't want a slow and frustrating deployment option (as in 10-20 min Azure Web Role deployments/upgrades vs a 30s Web Deploy command) or to be locked into deploying only one web site on your roles then this is the project for you.

Also, if you want to support the execution of background tasks (via console applications) alongside your web farm without having to set up separate Worker Roles or separate deployment pipelines this library will support that out-of-the-box.

## Documentation ##

### Web Farm Installation and Setup ###
1. Create a new ASP.NET MVC 4 website in Visual Studio using the blank template and delete the Global.asax.cs, Global.asax, Web.config and Views/Web.config files.
2. `Install-Package AzureWebFarm`
3. (optional) use ReSharper (or similar) to change the namespaces to match your assembly namespace
4. Ensure all the files in the StartUp folder are marked Copy Always
5. Ensure App.config gets copied to bin/ProjectName.dll.config before the Azure package is created using something like:

          <PropertyGroup>
            <WebProjectName>AzureWebFarm.Example.Web</WebProjectName>
          </PropertyGroup>
          <Target Name="CopyAppConfigurationIntoPackage" BeforeTargets="AfterPackageComputeService" Condition="$(Env)!=''">
            <Copy SourceFiles="$(ProjectDir)..\$(WebProjectName)\App.config" DestinationFiles="$(ProjectDir)obj\$(Configuration)\$(WebProjectName)\bin\$(WebProjectName).dll.config" />
          </Target>

6. Create a cloud project with the website as a web role
7. Look in the packages/AzureWebFarm/tools/ExampleConfigs folder to see example values to put in the .csdef and .cscfg files for it to work
8. Look in the packages/AzureWebFarm/tools/AdminConsole folder to run the AdminConsole.exe console application to configure your web farm to add / edit / delete websites and bindings

If you get lost check out the AzureWebFarm.Example.Web and AzureWebFarm.Example.Cloud projects for guidance.

### Background Worker Setup ###
If there is a subfolder within the bin directory of your website that contains a `.exe` file of the same name as the subfolder, e.g. `MySiteRoot\bin\SubFolder\SubFolder.exe` then the contents of the sub folder will be copied to an isolated folder and the executable file will be run.

If the executable exits with a non-zero exit code (e.g. when throwing an exception) then it will be automatically re-run. If this sounds familiar to you then yes, it is inspired by the way [App Harbour workers function](http://support.appharbor.com/kb/getting-started/background-workers).

It is recommended that you only perform lightweight functions in your background worker applications so that you don't overload your web farm servers. If you are doing intense background processing then you should use dedicated Worker Roles. This feature exists so that if you have lightweight tasks that need to be performed asynchronously and / or continuously to support your website you aren't burdened with having to create a new set of infrastructure and a deployment pipeline. This also allows you to take advantage of the fast deployment you get with your websites as well as keeping the console applications versioned alongside your website.

The `web.config` file from the website will be copied into the execution directory so you can [make use of it](http://stackoverflow.com/questions/1049868/how-to-load-config-file-programmatically) to prevent the need to respecify connection strings and the like across all your applications.

If you want to have a console application that runs periodically then simply include an infinite loop in your console application with a `Thread.Sleep` or similar to set the loop period. When you update your website the existing console application will be killed, replaced with the latest version and restarted.

You must ensure that your console application can handle being run simultaneously on multiple servers since it will be running on every server in your web farm. You must also ensure that it is resilient to being shutdown and restarted at any point in time since whenever you deploy your website that is what will happen to all the console applications for that website.

### Including a Background Worker in the web deployment package ###

The easiest way to do this is to use a bit of MSBuild in your web project such as (assumes the `.exe` output is the same name as the project and the console app project sub folder is at the same level as the web project subfolder in your solution, also assumes the web deploy is being generated in the default directory):

	  <Target Name="AddBackgroundWorker" BeforeTargets="PackageUsingManifest">
	    <Message Text="Copying Background Worker files into package temp path so it's copied into web deploy package." />
	    <ItemGroup>
		  <WorkerFiles Include="$(ProjectDir)..\MyBackgroundWorkerProjectDirectory\bin\$(Configuration)\*.*" />
	    </ItemGroup>
	    <Copy SourceFiles="@(WorkerFiles)" DestinationFolder="$(BaseIntermediateOutputPath)\$(Configuration)\Package\PackageTmp\bin\MyBackgroundWorkerProjectDirectory" />
	  </Target>

## Contributions ##
If you would like to contribute to this project then feel free to communicate with myself via Twitter [@robdmoore](http://twitter.com/robdmoore) or alternatively send a pull request.

## Changelog ##

### Version 0.9.1.1 ###
* Fixed potential NRE in the worker role (exposed by race condition)
* Fixed other potential race conditions

### Version 0.9.1 ###
* Added support to automatically execute lightweight console applications
* Remove the ability to manage via frontend and instead provided a console application

### Version 0.9.0 ###
* Initial release - slightly refactored from last version of Windows Azure Accelerator for Web Roles

## Roadmap ##
* [In progress] Manage setup and maintenance of application via NuGet package
* [In progress] Provide unit test coverage across most of the code
* Allow pre- and/or post-sync MsDeploy commands to manage IIS instead of using console app?
* Add the concept of a version of the site rather than scanning for last modified date across the site files
* Add status reporting and a dashboard for all roles in operation and the version they currently have
* If possible, upload the Web Deploy log when syncing a package so it can be inspected via the dashboard
* Investigate putting Kudu in
* Update the WPI exe in Startup and ensure the Web Deploy packages being used are the latest (use Chocolatey instead?)
* Install .NET 4.5 / support Azure 2.0 libs / support Windows Server 2012 Web Roles
* IL-merge Microsoft.Web.Deployment and Microsoft.Web.Administration (maybe just leave them as-is though...)?
* Support environment-based config transforms out of the box for web.config, app.config, servicedefinition.csdef and serviceconfiguration.cscfg
* Make debugging background workers easier by logging console output and any exceptions to table storage
* Use checksum comparison for syncing the package and capture the trace output so it can be logged somewhere for inspection
