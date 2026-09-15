using System.Reflection;
using System.Xml.Linq;
using PropFlow.Modules.AiClassification.Domain.Classifications;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Domain.Notifications;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Payments.Domain.Payments;

namespace PropFlow.ArchitectureTests;

public class ModuleBoundaryTests
{
    private static readonly string SolutionDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Solution_ShouldContainExactly22Projects()
    {
        var solutionFile = Path.Combine(SolutionDirectory, "PropFlow.sln");
        Assert.True(File.Exists(solutionFile), $"Solution file not found at: {solutionFile}");

        var lines = File.ReadAllLines(solutionFile);
        // Lọc bỏ solution folder entries (GUID 2150E333-8FDC-42A3-9474-1A3956D46DE8)
        // Chỉ đếm các project thực (.csproj)
        const string SolutionFolderGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";
        var projectLines = lines
            .Where(l => l.StartsWith("Project(") && !l.Contains(SolutionFolderGuid))
            .ToList();

        Assert.Equal(22, projectLines.Count);
    }

    [Fact]
    public void Batch1_Modules_ShouldNotReferenceEachOther()
    {
        var batch1Projects = new[]
        {
            "src/Modules/PropertyAssets/PropFlow.Modules.PropertyAssets.csproj",
            "src/Modules/Apartments/PropFlow.Modules.Apartments.csproj",
            "src/Modules/Authentication/PropFlow.Modules.Authentication.csproj",
            "src/Modules/Residents/PropFlow.Modules.Residents.csproj"
        };

        foreach (var projectRelPath in batch1Projects)
        {
            var projectFile = Path.Combine(SolutionDirectory, projectRelPath);
            Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

            var doc = XDocument.Load(projectFile);
            var projectReferences = doc.Descendants("ProjectReference")
                .Select(pr => pr.Attribute("Include")?.Value)
                .Where(v => v != null)
                .ToList();

            // No module-to-module project references allowed
            foreach (var pr in projectReferences)
            {
                Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Batch1_DomainEntities_ShouldNotDependOnAspNetCore()
    {
        var batch1Assemblies = new[]
        {
            typeof(Building).Assembly,
            typeof(ApartmentUnit).Assembly,
            typeof(UserAccount).Assembly,
            typeof(Resident).Assembly
        };

        foreach (var assembly in batch1Assemblies)
        {
            var domainTypes = assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
                .ToList();

            foreach (var type in domainTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    Assert.DoesNotContain("Microsoft.AspNetCore", prop.PropertyType.FullName ?? string.Empty);
                }

                foreach (var field in fields)
                {
                    Assert.DoesNotContain("Microsoft.AspNetCore", field.FieldType.FullName ?? string.Empty);
                }
            }
        }
    }

    [Fact]
    public void AuthenticationDomain_ShouldNotDependOnVerificationProvidersOrAspNetCore()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "Smtp",
            "Gmail",
            "Twilio",
            "Firebase"
        };

        var authDomainTypes = typeof(UserAccount).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in authDomainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void AuthenticationModule_ShouldNotReferenceResidentsModule()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Authentication/PropFlow.Modules.Authentication.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        Assert.DoesNotContain(projectReferences, pr => pr!.Contains("Residents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthenticationModule_ShouldUseUserAccount_AndNotHaveUserEntity()
    {
        var authAssembly = typeof(UserAccount).Assembly;

        var userAccountType = authAssembly.GetType("PropFlow.Modules.Authentication.Domain.Users.UserAccount");
        Assert.NotNull(userAccountType);

        var userType = authAssembly.GetType("PropFlow.Modules.Authentication.Domain.Users.User");
        Assert.Null(userType);
    }

    [Fact]
    public void PropFlowShared_ShouldNotContainBusinessEntities()
    {
        var sharedAssembly = typeof(PropFlow.Shared.ClassPlaceholder).Assembly;
        var types = sharedAssembly.GetTypes().Where(t => !t.IsNestedPrivate).ToList();

        // PropFlow.Shared must not contain business entities or enums
        foreach (var type in types)
        {
            Assert.DoesNotContain("Building", type.Name);
            Assert.DoesNotContain("Resident", type.Name);
            Assert.DoesNotContain("Apartment", type.Name);
            Assert.DoesNotContain("Invoice", type.Name);
            Assert.DoesNotContain("User", type.Name);
            Assert.DoesNotContain("Payment", type.Name);
        }
    }

    [Fact]
    public void Batch1_DomainEntities_ShouldNotHaveCrossModuleNavigationProperties()
    {
        var batch1Assemblies = new[]
        {
            (Assembly: typeof(Building).Assembly, ModuleName: "PropertyAssets"),
            (Assembly: typeof(ApartmentUnit).Assembly, ModuleName: "Apartments"),
            (Assembly: typeof(UserAccount).Assembly, ModuleName: "Authentication"),
            (Assembly: typeof(Resident).Assembly, ModuleName: "Residents")
        };

        foreach (var (assembly, moduleName) in batch1Assemblies)
        {
            var domainEntities = assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain") && t.IsClass)
                .ToList();

            foreach (var entityType in domainEntities)
            {
                var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    var propType = prop.PropertyType;
                    // Check if it's a collection
                    if (propType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
                    {
                        propType = propType.GetGenericArguments().FirstOrDefault() ?? propType;
                    }

                    if (propType.Namespace != null && propType.Namespace.StartsWith("PropFlow.Modules."))
                    {
                        // Ensure it belongs to the same module
                        Assert.Contains(moduleName, propType.Namespace);
                    }
                }
            }
        }
    }

    [Fact]
    public void Administration_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Administration/PropFlow.Modules.Administration.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Administration_DomainEntities_ShouldNotDependOnAspNetCore()
    {
        var domainTypes = typeof(Role).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                Assert.DoesNotContain("Microsoft.AspNetCore", prop.PropertyType.FullName ?? string.Empty);
            }

            foreach (var field in fields)
            {
                Assert.DoesNotContain("Microsoft.AspNetCore", field.FieldType.FullName ?? string.Empty);
            }
        }
    }

    [Fact]
    public void Administration_DomainEntities_ShouldNotDependOnExternalProvidersOrApi()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Smtp",
            "Gmail",
            "Twilio",
            "Firebase"
        };

        var domainTypes = typeof(Role).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Administration_DomainEntities_ShouldNotHaveCrossModuleNavigationProperties()
    {
        var domainEntities = typeof(Role).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain") && t.IsClass)
            .ToList();

        foreach (var entityType in domainEntities)
        {
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var propType = prop.PropertyType;
                if (propType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
                {
                    propType = propType.GetGenericArguments().FirstOrDefault() ?? propType;
                }

                if (propType.Namespace != null && propType.Namespace.StartsWith("PropFlow.Modules."))
                {
                    Assert.Contains("Administration", propType.Namespace);
                }
            }
        }
    }

    [Fact]
    public void Administration_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "Administration", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialAdministration*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAdministration.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAdministration.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ShouldReferenceOnlyImplementedPersistenceModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value?.Replace('\\', '/'))
            .Where(v => v != null)
            .ToList();

        var expectedReferences = new[]
        {
            "../Modules/PropertyAssets/PropFlow.Modules.PropertyAssets.csproj",
            "../Modules/Apartments/PropFlow.Modules.Apartments.csproj",
            "../Modules/Authentication/PropFlow.Modules.Authentication.csproj",
            "../Modules/Residents/PropFlow.Modules.Residents.csproj",
            "../Modules/Administration/PropFlow.Modules.Administration.csproj",
            "../Modules/ServiceRequests/PropFlow.Modules.ServiceRequests.csproj",
            "../Modules/Complaints/PropFlow.Modules.Complaints.csproj",
            "../Modules/Maintenance/PropFlow.Modules.Maintenance.csproj",
            "../Modules/Billing/PropFlow.Modules.Billing.csproj",
            "../Modules/Payments/PropFlow.Modules.Payments.csproj",
            "../Modules/AiClassification/PropFlow.Modules.AiClassification.csproj",
            "../Modules/AiRecommendation/PropFlow.Modules.AiRecommendation.csproj",
            "../Modules/Communication/PropFlow.Modules.Communication.csproj"
        };

        Assert.Equal(expectedReferences.Length, projectReferences.Count);
        foreach (var expectedReference in expectedReferences)
        {
            Assert.Contains(expectedReference, projectReferences);
        }
    }

    [Fact]
    public void ServiceRequestsAndComplaints_Modules_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFiles = new[]
        {
            "src/Modules/ServiceRequests/PropFlow.Modules.ServiceRequests.csproj",
            "src/Modules/Complaints/PropFlow.Modules.Complaints.csproj"
        };

        foreach (var projectRelPath in projectFiles)
        {
            var projectFile = Path.Combine(SolutionDirectory, projectRelPath);
            Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

            var doc = XDocument.Load(projectFile);
            var projectReferences = doc.Descendants("ProjectReference")
                .Select(pr => pr.Attribute("Include")?.Value)
                .Where(v => v != null)
                .ToList();

            foreach (var pr in projectReferences)
            {
                Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ServiceRequestsAndComplaints_ShouldHaveInitialMigrationFiles()
    {
        var migrationChecks = new[]
        {
            (Dir: Path.Combine(SolutionDirectory, "src", "Modules", "ServiceRequests", "Infrastructure", "Persistence", "Migrations"), Name: "InitialServiceRequests"),
            (Dir: Path.Combine(SolutionDirectory, "src", "Modules", "Complaints", "Infrastructure", "Persistence", "Migrations"), Name: "InitialComplaints")
        };

        foreach (var (migrationsDir, migrationName) in migrationChecks)
        {
            Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");
            var migrationFiles = Directory.GetFiles(migrationsDir, $"*{migrationName}*.cs");
            Assert.NotEmpty(migrationFiles);
            Assert.Contains(migrationFiles, path => path.EndsWith($"_{migrationName}.cs", StringComparison.Ordinal));
            Assert.Contains(migrationFiles, path => path.EndsWith($"_{migrationName}.Designer.cs", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Api_ShouldReferenceServiceRequestsAndComplaintsAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("ServiceRequests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, pr => pr.Contains("Complaints", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Maintenance_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Maintenance/PropFlow.Modules.Maintenance.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Maintenance_DomainEntities_ShouldNotDependOnAspNetCoreOrExternalProviders()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Smtp",
            "Gmail",
            "Twilio",
            "Firebase"
        };

        var domainTypes = typeof(MaintenanceTask).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Billing_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Billing/PropFlow.Modules.Billing.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Billing_DomainEntities_ShouldNotDependOnAspNetCoreOrExternalProviders()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Smtp",
            "Gmail",
            "Twilio",
            "Firebase"
        };

        var domainTypes = typeof(Invoice).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Payments_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Payments/PropFlow.Modules.Payments.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Payments_DomainEntities_ShouldNotDependOnAspNetCoreOrExternalProviders()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Smtp",
            "Gmail",
            "Twilio",
            "Firebase",
            "VNPay",
            "MoMo",
            "ZaloPay"
        };

        var domainTypes = typeof(Payment).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void AiClassification_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/AiClassification/PropFlow.Modules.AiClassification.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AiClassification_DomainEntities_ShouldNotDependOnAspNetCoreInfrastructureOrAiSdks()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Infrastructure",
            "OpenAI",
            "Azure.AI",
            "Anthropic",
            "SemanticKernel",
            "HttpClient"
        };

        var domainTypes = typeof(AiRequestClassification).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void OperationalModules_DomainEntities_ShouldNotHaveCrossModuleNavigationProperties()
    {
        var moduleAssemblies = new[]
        {
            (Assembly: typeof(ServiceRequest).Assembly, ModuleName: "ServiceRequests"),
            (Assembly: typeof(Complaint).Assembly, ModuleName: "Complaints"),
            (Assembly: typeof(MaintenanceTask).Assembly, ModuleName: "Maintenance"),
            (Assembly: typeof(Invoice).Assembly, ModuleName: "Billing"),
            (Assembly: typeof(Payment).Assembly, ModuleName: "Payments"),
            (Assembly: typeof(AiRequestClassification).Assembly, ModuleName: "AiClassification"),
            (Assembly: typeof(AiRequestRecommendation).Assembly, ModuleName: "AiRecommendation"),
            (Assembly: typeof(Notification).Assembly, ModuleName: "Communication")
        };

        foreach (var (assembly, moduleName) in moduleAssemblies)
        {
            var domainEntities = assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain") && t.IsClass)
                .ToList();

            foreach (var entityType in domainEntities)
            {
                var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    var propType = prop.PropertyType;
                    if (propType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
                    {
                        propType = propType.GetGenericArguments().FirstOrDefault() ?? propType;
                    }

                    if (propType.Namespace != null && propType.Namespace.StartsWith("PropFlow.Modules."))
                    {
                        Assert.Contains(moduleName, propType.Namespace);
                    }
                }
            }
        }
    }

    [Fact]
    public void Maintenance_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "Maintenance", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialMaintenance*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialMaintenance.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialMaintenance.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ShouldReferenceMaintenanceAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("Maintenance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Billing_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "Billing", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialBilling*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialBilling.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialBilling.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ShouldReferenceBillingAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("Billing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Payments_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "Payments", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialPayments*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialPayments.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialPayments.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ShouldReferencePaymentsAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("Payments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AiClassification_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "AiClassification", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialAiClassification*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAiClassification.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAiClassification.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AiClassification_ShouldHaveCorrectiveDomainConsistencyMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "AiClassification", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*CorrectAiClassificationDomainConsistency*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_CorrectAiClassificationDomainConsistency.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_CorrectAiClassificationDomainConsistency.Designer.cs", StringComparison.Ordinal));

        var migrationFile = migrationFiles.Single(path => path.EndsWith("_CorrectAiClassificationDomainConsistency.cs", StringComparison.Ordinal));
        var migrationText = File.ReadAllText(migrationFile);
        Assert.Contains("CK_ai_request_classifications_success_fields", migrationText);
        Assert.Contains("CK_ai_request_classifications_failed_fields", migrationText);
    }

    [Fact]
    public void AiClassification_Domain_ShouldNotExposeArbitraryStatusOrReviewMutation()
    {
        var publicMethods = typeof(AiRequestClassification)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(publicMethods, method =>
            method.Name is "SetStatus" or "ChangeStatus" or "UpdateStatus" or "RecordFailure" or "OverrideClassification");
        Assert.DoesNotContain(publicMethods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(PropFlow.Modules.AiClassification.Domain.Classifications.AiRunStatus) ||
            parameter.ParameterType == typeof(PropFlow.Modules.AiClassification.Domain.Classifications.AiReviewDecision)));
    }

    [Fact]
    public void AiRecommendation_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/AiRecommendation/PropFlow.Modules.AiRecommendation.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AiRecommendation_DomainEntities_ShouldNotDependOnAspNetCoreInfrastructureOrAiSdks()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Infrastructure",
            "OpenAI",
            "Azure.AI",
            "Anthropic",
            "SemanticKernel",
            "HttpClient"
        };

        var domainTypes = typeof(AiRequestRecommendation).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void AiRecommendation_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "AiRecommendation", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialAiRecommendation*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAiRecommendation.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialAiRecommendation.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AiRecommendation_Domain_ShouldNotExposeArbitraryStatusOrReviewMutation()
    {
        var publicMethods = typeof(AiRequestRecommendation)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(publicMethods, method =>
            method.Name is "SetStatus" or "ChangeStatus" or "UpdateStatus" or "RecordFailure" or "OverrideRecommendation" or "RejectRecommendation");
        Assert.DoesNotContain(publicMethods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(PropFlow.Modules.AiRecommendation.Domain.Recommendations.AiRunStatus) ||
            parameter.ParameterType == typeof(PropFlow.Modules.AiRecommendation.Domain.Recommendations.AiReviewDecision)));
    }

    [Fact]
    public void Api_ShouldReferenceAiClassificationAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("AiClassification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Api_ShouldReferenceAiRecommendationAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("AiRecommendation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Communication_Module_ShouldNotReferenceOtherBusinessModules()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/Modules/Communication/PropFlow.Modules.Communication.csproj");
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");

        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();

        foreach (var pr in projectReferences)
        {
            Assert.DoesNotContain("Modules", pr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Communication_DomainEntities_ShouldNotDependOnAspNetCoreInfrastructureOrDeliverySdks()
    {
        var forbiddenNamespaceParts = new[]
        {
            "Microsoft.AspNetCore",
            "PropFlow.Api",
            "Infrastructure",
            "MailKit",
            "Twilio",
            "Firebase",
            "SignalR",
            "Smtp",
            "HttpClient"
        };

        var domainTypes = typeof(Notification).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains(".Domain"))
            .ToList();

        foreach (var type in domainTypes)
        {
            var referencedTypeNames = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member switch
                {
                    FieldInfo field => field.FieldType.FullName ?? string.Empty,
                    PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
                    MethodInfo method => method.ReturnType.FullName ?? string.Empty,
                    _ => string.Empty
                });

            foreach (var referencedTypeName in referencedTypeNames)
            {
                foreach (var forbidden in forbiddenNamespaceParts)
                {
                    Assert.DoesNotContain(forbidden, referencedTypeName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Communication_Domain_ShouldNotExposeHardDeleteOrGenericStatusMutation()
    {
        var notificationMethods = typeof(Notification)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var announcementMethods = typeof(Announcement)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var method in notificationMethods.Concat(announcementMethods))
        {
            Assert.DoesNotMatch("^(Delete|ClearHistory|Purge|SetStatus|ChangeStatus|UpdateStatus)$", method.Name);
        }
    }

    [Fact]
    public void Communication_ShouldHaveInitialMigrationFiles()
    {
        var migrationsDir = Path.Combine(SolutionDirectory, "src", "Modules", "Communication", "Infrastructure", "Persistence", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*InitialCommunication*.cs");
        Assert.NotEmpty(migrationFiles);
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialCommunication.cs", StringComparison.Ordinal));
        Assert.Contains(migrationFiles, path => path.EndsWith("_InitialCommunication.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ShouldReferenceCommunicationAfterDatabaseModelApproval()
    {
        var projectFile = Path.Combine(SolutionDirectory, "src/PropFlow.Api/PropFlow.Api.csproj");
        var doc = XDocument.Load(projectFile);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(projectReferences, pr => pr.Contains("Communication", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, pr => pr.Contains("AiChatbot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceRequestsAndComplaints_ModelDesign_ShouldIntroduceExactlySevenDomainEntities()
    {
        var entityTypes = new[]
        {
            "PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories.ServiceRequestCategory",
            "PropFlow.Modules.ServiceRequests.Domain.ServiceRequests.ServiceRequest",
            "PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments.ServiceRequestAssignment",
            "PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities.ServiceRequestActivity",
            "PropFlow.Modules.Complaints.Domain.Complaints.Complaint",
            "PropFlow.Modules.Complaints.Domain.ComplaintFollowups.ComplaintFollowup",
            "PropFlow.Modules.Complaints.Domain.ComplaintActivities.ComplaintActivity"
        };

        foreach (var typeName in entityTypes)
        {
            var assembly = typeName.Contains(".ServiceRequests.", StringComparison.Ordinal)
                ? typeof(ServiceRequest).Assembly
                : typeof(Complaint).Assembly;

            Assert.NotNull(assembly.GetType(typeName));
        }
    }
}
