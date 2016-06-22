using System.Collections.Generic;
using System.Linq;
using NuGet.Resolver;
using NuGet.Resources;
using System;

namespace NuGet
{
    public class InstallPackageUtility
    {
        public InstallPackageUtility(ActionResolver resolver)
        {
            Resolver = resolver;
            Logger = NullLogger.Instance;
        }

        public ActionResolver Resolver { get; private set; }
        public bool Safe { get; set; }
        public ILogger Logger { get; set; }
        public bool AllowPrereleaseVersions { get; set; }

        public IEnumerable<Resolver.PackageAction> ResolveActionsForInstallPackage(string id, SemanticVersion version,
            IEnumerable<IProjectManager> projectManagers, bool projectNameSpecified)
        {
            if (string.IsNullOrEmpty(id))
            {
                return ResolveActionsToInstallAllPackages(projectManagers);
            }
            else
            {
                return ResolveActionsToInstallOnePackage(id, version, projectManagers, projectNameSpecified);
            }
        }

        IEnumerable<Resolver.PackageAction> ResolveActionsToInstallAllPackages(IEnumerable<IProjectManager> projectManagers)
        {
            // BUGBUG: TargetFramework should be passed for more efficient package walking
            var packageSorter = new PackageSorter(targetFramework: null);
            // Get the packages in reverse dependency order then run update on each one i.e. if A -> B run Update(A) then Update(B)
            var packages = packageSorter.GetPackagesByDependencyOrder(
                projectManagers.First().PackageManager.LocalRepository).Reverse();

            foreach (var projectManager in projectManagers)
            {
                foreach (var package in packages)
                {
                    AddUpdateOperations(
                        package.Id,
                        null,
                        new[] { projectManager });
                }
            }

            var actions = Resolver.ResolveActions();
            return actions;
        }

        // Add update operations to the resolver
        void AddUpdateOperations(
            string id,
            SemanticVersion version,
            IEnumerable<IProjectManager> projectManagers)
        {
            if (!Safe)
            {
                // Update to latest version                
                foreach (var projectManager in projectManagers)
                {
                    AddUnsafeUpdateOperation(
                        id,
                        version,
                        version != null,
                        projectManager);
                }
            }
            else
            {
                // safe update
                foreach (var projectManager in projectManagers)
                {
                    IPackage installedPackage = projectManager.LocalRepository.FindPackage(id);
                    if (installedPackage == null)
                    {
                        continue;
                    }

                    var safeRange = VersionUtility.GetSafeRange(installedPackage.Version);
                    var package = projectManager.PackageManager.SourceRepository.FindPackage(
                        id,
                        safeRange,
                        projectManager.ConstraintProvider,
                        AllowPrereleaseVersions,
                        allowUnlisted: false);

                    Resolver.AddOperation(PackageAction.Install, package, projectManager);
                }
            }
        }

        void AddUnsafeUpdateOperation(
            string id,
            SemanticVersion version,
            bool targetVersionSetExplicitly,
            IProjectManager projectManager)
        {
            Logger.Log(MessageLevel.Debug, NuGetResources.Debug_LookingForUpdates, id);

            var package = projectManager.PackageManager.SourceRepository.FindPackage(
                id, version,
                projectManager.ConstraintProvider,
                AllowPrereleaseVersions,
                allowUnlisted: false);

            if (package != null)
            {
                Logger.Log(MessageLevel.Info, NuGetResources.Log_UpdatingPackages,
                    package.Id,
                    package.Version,
                    projectManager.Project.ProjectName);

                Resolver.AddOperation(PackageAction.Install, package, projectManager);
            }
            else
            {
                throw new Exception(String.Format(NuGetResources.InvalidVersionString, version.ToString()));
            }

            // Display message that no updates are available.
            IVersionSpec constraint = projectManager.ConstraintProvider.GetConstraint(package.Id);
            if (constraint != null)
            {
                Logger.Log(MessageLevel.Info, NuGetResources.Log_ApplyingConstraints, package.Id, VersionUtility.PrettyPrint(constraint),
                    projectManager.ConstraintProvider.Source);
            }

            Logger.Log(
                MessageLevel.Info,
                NuGetResources.Log_NoUpdatesAvailableForProject,
                package.Id,
                projectManager.Project.ProjectName);
        }

        // Updates the specified package in projects        
        IEnumerable<Resolver.PackageAction> ResolveActionsToInstallOnePackage(string id, SemanticVersion version, IEnumerable<IProjectManager> projectManagers,
            bool projectNameSpecified)
        {
            var packageManager = projectManagers.First().PackageManager;

            AddUpdateOperations(
                    id,
                    version,
                    projectManagers);

            return Resolver.ResolveActions();
        }
    }
}
