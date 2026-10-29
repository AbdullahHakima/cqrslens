using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CqrsLens;

/// <summary>
/// Creates the Roslyn syntax trees and compilation used for one scan. Real source is parsed first;
/// minimal contract stubs are added only for MediatR/Mediator/FluentValidation/EF types the source
/// does not already define, so common applications bind without package references.
/// </summary>
internal static class RoslynCompilationBuilder
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences =
        new(CreateRuntimeReferences);

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    internal static SyntaxTree ParseSourceFile(
        SourceFile file,
        IReadOnlyDictionary<string, List<UsingDirective>> injectedByPath)
    {
        var code = UsingDirectiveParser.StripGlobalUsingKeywords(file.Code);
        if (injectedByPath.TryGetValue(file.Path, out var injected) && injected.Count > 0)
        {
            if (code.Length > 0 && code[0] == '\uFEFF')
                code = code[1..];
            var preamble = string.Join("\n", injected.Select(u => u.ToUsingDeclaration())) + "\n#line 1\n";
            code = preamble + code;
        }

        return CSharpSyntaxTree.ParseText(code, ParseOptions, file.Path);
    }

    internal static Compilation CreateCompilation(IEnumerable<SyntaxTree> sourceTrees)
    {
        var trees = sourceTrees.ToList();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true);

        // Probe the real source first. If a project already declares one of the
        // contracts (unit-test shims or a vendored copy), never add a duplicate.
        var probe = CSharpCompilation.Create(
            "CqrsLens.RoslynScan.Probe",
            trees,
            RuntimeReferences.Value,
            options);

        var stubs = BuildStubSource(probe);
        if (stubs.Length == 0)
        {
            trees.Add(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "CqrsLens.GlobalUsings.g.cs"));
            return CSharpCompilation.Create(
                "CqrsLens.RoslynScan",
                trees,
                RuntimeReferences.Value,
                options);
        }

        trees.Add(CSharpSyntaxTree.ParseText(stubs, ParseOptions, "CqrsLens.RoslynContracts.g.cs"));
        trees.Add(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "CqrsLens.GlobalUsings.g.cs"));
        return CSharpCompilation.Create(
            "CqrsLens.RoslynScan",
            trees,
            RuntimeReferences.Value,
            options);
    }

    /// <summary>
    /// Real applications reference MediatR/FluentValidation/EF Core assemblies that
    /// are not part of the runtime reference set. The text scanner already matches
    /// these shapes by name; Roslyn needs symbols to do better. Add minimal contract
    /// shapes only for contracts the source does not define itself.
    /// </summary>
    private static string BuildStubSource(Compilation probe)
    {
        var sb = new StringBuilder();
        var mediatR = new List<string>();

        if (probe.GetTypeByMetadataName("MediatR.IRequest") is null)
            mediatR.Add("public interface IRequest { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequest`1") is null)
            mediatR.Add("public interface IRequest<out TResponse> { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequestHandler`1") is null)
            mediatR.Add("public interface IRequestHandler<in TRequest> where TRequest : IRequest { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequestHandler`2") is null)
            mediatR.Add("public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("MediatR.IPipelineBehavior`2") is null)
            mediatR.Add("public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull { }");
        if (probe.GetTypeByMetadataName("MediatR.RequestHandlerDelegate`1") is null)
            mediatR.Add("public delegate System.Threading.Tasks.Task<TResponse> RequestHandlerDelegate<TResponse>();");
        if (probe.GetTypeByMetadataName("MediatR.INotification") is null)
            mediatR.Add("public interface INotification { }");
        if (probe.GetTypeByMetadataName("MediatR.INotificationHandler`1") is null)
            mediatR.Add("public interface INotificationHandler<in TNotification> where TNotification : INotification { }");

        AppendNamespace(sb, "MediatR", mediatR);

        var mediator = new List<string>();
        if (probe.GetTypeByMetadataName("Mediator.IMessage") is null)
            mediator.Add("public interface IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamMessage") is null)
            mediator.Add("public interface IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseRequest") is null)
            mediator.Add("public interface IBaseRequest : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequest") is null)
            mediator.Add("public interface IRequest : IBaseRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequest`1") is null)
            mediator.Add("public interface IRequest<out TResponse> : IBaseRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseCommand") is null)
            mediator.Add("public interface IBaseCommand : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommand") is null)
            mediator.Add("public interface ICommand : IBaseCommand { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommand`1") is null)
            mediator.Add("public interface ICommand<out TResponse> : IBaseCommand { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseQuery") is null)
            mediator.Add("public interface IBaseQuery : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IQuery`1") is null)
            mediator.Add("public interface IQuery<out TResponse> : IBaseQuery { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamRequest`1") is null)
            mediator.Add("public interface IStreamRequest<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamCommand`1") is null)
            mediator.Add("public interface IStreamCommand<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamQuery`1") is null)
            mediator.Add("public interface IStreamQuery<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequestHandler`1") is null)
            mediator.Add("public interface IRequestHandler<in TRequest> where TRequest : IRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequestHandler`2") is null)
            mediator.Add("public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommandHandler`1") is null)
            mediator.Add("public interface ICommandHandler<in TCommand> where TCommand : ICommand { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommandHandler`2") is null)
            mediator.Add("public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IQueryHandler`2") is null)
            mediator.Add("public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamRequestHandler`2") is null)
            mediator.Add("public interface IStreamRequestHandler<in TRequest, TResponse> where TRequest : IStreamRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamCommandHandler`2") is null)
            mediator.Add("public interface IStreamCommandHandler<in TCommand, TResponse> where TCommand : IStreamCommand<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamQueryHandler`2") is null)
            mediator.Add("public interface IStreamQueryHandler<in TQuery, TResponse> where TQuery : IStreamQuery<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IPipelineBehavior`2") is null)
            mediator.Add("public interface IPipelineBehavior<TMessage, TResponse> where TMessage : notnull { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamPipelineBehavior`2") is null)
            mediator.Add("public interface IStreamPipelineBehavior<TMessage, TResponse> where TMessage : notnull { }");
        if (probe.GetTypeByMetadataName("Mediator.MessageHandlerDelegate`2") is null)
            mediator.Add("public delegate System.Threading.Tasks.ValueTask<TResponse> MessageHandlerDelegate<TMessage, TResponse>(TMessage message, System.Threading.CancellationToken cancellationToken);");
        if (probe.GetTypeByMetadataName("Mediator.INotification") is null)
            mediator.Add("public interface INotification : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.INotificationHandler`1") is null)
            mediator.Add("public interface INotificationHandler<in TNotification> where TNotification : INotification { }");
        AppendNamespace(sb, "Mediator", mediator);

        var fluent = new List<string>();
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator") is null)
            fluent.Add("public interface IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator`1") is null)
            fluent.Add("public interface IValidator<in T> : IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is null)
            fluent.Add("public abstract class AbstractValidator<T> : IValidator<T> { protected AbstractValidator() { } }");
        AppendNamespace(sb, "FluentValidation", fluent);

        var fastEndpoints = new List<string>();
        if (probe.GetTypeByMetadataName("FastEndpoints.Validator`1") is null)
            fastEndpoints.Add(
                "public abstract class Validator<T> : FluentValidation.AbstractValidator<T> { protected Validator() { } }");
        AppendNamespace(sb, "FastEndpoints", fastEndpoints);

        var ef = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") is null)
            ef.Add("public class DbContext { protected DbContext() { } protected DbContext(object? options) { } protected virtual void OnModelCreating(ModelBuilder modelBuilder) { } }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1") is null)
            ef.Add("public abstract class DbSet<TEntity> where TEntity : class { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1") is null)
            ef.Add("public class DbContextOptions<TContext> where TContext : DbContext { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.ModelBuilder") is null)
            ef.Add("public class ModelBuilder { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1") is null)
            ef.Add("public interface IEntityTypeConfiguration<TEntity> where TEntity : class { void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> builder); }");
        AppendNamespace(sb, "Microsoft.EntityFrameworkCore", ef);

        var efBuilders = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder`1") is null)
            efBuilders.Add("public class EntityTypeBuilder<TEntity> where TEntity : class { }");
        AppendNamespace(sb, "Microsoft.EntityFrameworkCore.Metadata.Builders", efBuilders);

        var identity = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext") is null)
            identity.Add("public class IdentityDbContext : Microsoft.EntityFrameworkCore.DbContext { }");
        if (probe.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext`1") is null)
            identity.Add("public class IdentityDbContext<TUser> : Microsoft.EntityFrameworkCore.DbContext where TUser : class { }");
        AppendNamespace(sb, "Microsoft.AspNetCore.Identity.EntityFrameworkCore", identity);

        return sb.ToString();
    }

    private static void AppendNamespace(StringBuilder sb, string ns, List<string> members)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        foreach (var member in members)
            sb.AppendLine("    " + member);
        sb.AppendLine("}");
    }

    private static IReadOnlyList<MetadataReference> CreateRuntimeReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        return paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
