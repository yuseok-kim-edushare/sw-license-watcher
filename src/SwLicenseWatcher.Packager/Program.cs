using SwLicenseWatcher.Packager;
using SwLicenseWatcher.Setup.Core;

ApplicationConfiguration.Initialize();
Application.Run(new PackagerForm(new PackagingWorkflow(new CompanyPackager())));
