using System.Globalization;
using Scriban;
using Scriban.Runtime;

namespace Shellwright.Codegen.Templating;

/// <summary>Raised when a template fails to parse or render.</summary>
public sealed class TemplateException : Exception
{
    /// <summary>Creates an exception naming the template that failed.</summary>
    /// <param name="templateName">The template's relative path.</param>
    /// <param name="message">What went wrong.</param>
    public TemplateException(string templateName, string message)
        : base($"{templateName}: {message}") => TemplateName = templateName;

    /// <summary>Creates an empty exception.</summary>
    public TemplateException() => TemplateName = string.Empty;

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public TemplateException(string message) : base(message) => TemplateName = string.Empty;

    /// <summary>Creates an exception with a message and cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public TemplateException(string message, Exception innerException)
        : base(message, innerException) => TemplateName = string.Empty;

    /// <summary>The template's relative path.</summary>
    public string TemplateName { get; }
}

/// <summary>
/// Renders <c>.tmpl</c> files with Scriban.
/// </summary>
/// <remarks>
/// <para>
/// Scriban rather than a hand-rolled token replacer because templates need
/// loops — one <c>values-&lt;locale&gt;</c> directory per locale, one
/// intent-filter per deep-link host — and rather than Razor or T4 because it
/// executes no arbitrary code. A template is data that happens to be
/// committed, and it should not be able to do anything a data file could not.
/// </para>
/// <para>
/// ⚠️ <see cref="TemplateContext.StrictVariables"/> is on. Without it, a
/// mistyped or renamed config key renders as an empty string: the project still
/// generates, the golden file quietly records the loss, and the customer's app
/// ships with no name. Failing at render time turns that into a build error
/// with a line number.
/// </para>
/// </remarks>
public static class ScribanTemplateEngine
{
    /// <summary>Renders one template against a model.</summary>
    /// <param name="templateName">Relative path, used in error messages.</param>
    /// <param name="source">The template text.</param>
    /// <param name="model">The model from <see cref="TemplateModel"/>.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="TemplateException">The template failed to parse or render.</exception>
    public static string Render(string templateName, string source, ScriptObject model)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);

        var template = Template.Parse(source, templateName);

        if (template.HasErrors)
        {
            throw new TemplateException(
                templateName,
                string.Join("; ", template.Messages.Select(message => message.ToString())));
        }

        var context = new TemplateContext
        {
            StrictVariables = true,

            // The default renamer maps PascalCase members to snake_case, which
            // would silently rename every camelCase config key. Templates
            // should read exactly like the schema they are filled from.
            MemberRenamer = member => member.Name,

            // Generated files are compared byte for byte, so rendering must not
            // depend on the machine's locale.
            EnableRelaxedMemberAccess = false,
        };

        context.PushGlobal(model);
        context.PushCulture(CultureInfo.InvariantCulture);

        try
        {
            return template.Render(context);
        }
        catch (Scriban.Syntax.ScriptRuntimeException error)
        {
            throw new TemplateException(templateName, error.Message);
        }
    }
}
