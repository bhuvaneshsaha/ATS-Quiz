using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Shortlist.Adapters.Email.Smtp;
using Shortlist.Adapters.Storage.FileSystem;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Assessments.Application;
using Shortlist.Modules.Assessments.Contracts;
using Shortlist.Modules.Assessments.Domain;
using Shortlist.Modules.Assessments.Infrastructure;
using Shortlist.Modules.Core.Application;
using Shortlist.Modules.Core.Contracts;
using Shortlist.Modules.Core.Domain;
using Shortlist.Modules.Core.Infrastructure;
using Shortlist.Modules.Identity.Application;
using Shortlist.Modules.Identity.Domain;
using Shortlist.Modules.Identity.Infrastructure;
using Shouldly;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace Shortlist.Architecture.Tests;

public class DependencyRules
{
    private static readonly ArchitectureModel Model = new ArchLoader()
        .LoadAssemblies(
            typeof(Opening).Assembly,
            typeof(CreateOpening).Assembly,
            typeof(CoreDbContext).Assembly,
            typeof(ApplicationStageChanged).Assembly,
            typeof(Question).Assembly,
            typeof(CreateQuestion).Assembly,
            typeof(AssessmentsDbContext).Assembly,
            typeof(AssessmentInvited).Assembly,
            typeof(RoleAssignment).Assembly,
            typeof(SignIn).Assembly,
            typeof(IdentityDbContext).Assembly,
            typeof(Result).Assembly,
            typeof(LoggingEmailSender).Assembly,
            typeof(FileSystemFileStore).Assembly)
        .Build();

    [Fact]
    public void The_model_includes_assessment_types()
    {
        Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.Assessments(\\..*)?").GetObjects(Model).ShouldNotBeEmpty();
    }

    [Fact]
    public void Assessments_must_not_reference_core_internals()
    {
        IArchRule rule = Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.Assessments(\\..*)?")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.Core\\.(Domain|Application|Infrastructure)(\\..*)?"));
        rule.Check(Model);
    }

    [Fact]
    public void Core_must_not_reference_assessment_internals()
    {
        IArchRule rule = Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.Core(\\..*)?")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.Assessments\\.(Domain|Application|Infrastructure)(\\..*)?"));
        rule.Check(Model);
    }

    [Fact]
    public void Domain_and_application_do_not_reference_ef_or_adapters()
    {
        IArchRule rule = Types().That().ResideInNamespaceMatching("Shortlist\\.Modules\\.(Core|Assessments|Identity)\\.(Domain|Application)(\\..*)?")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching("Microsoft\\.EntityFrameworkCore(\\..*)?")
                    .Or().ResideInNamespaceMatching("Shortlist\\.Adapters(\\..*)?"));
        rule.Check(Model);
    }
}
