using System.Collections.Generic;

namespace ServerAbiCheck;

/// <summary>
/// What one check of a plugin against one server counted and found.
/// </summary>
internal sealed class AbiReport
{
    private readonly List<string> _problems = new();

    /// <summary>
    /// Gets or sets how many type references the plugin makes.
    /// </summary>
    public int TypeReferences { get; set; }

    /// <summary>
    /// Gets or sets how many member references (methods and fields) were checked.
    /// </summary>
    public int MemberReferences { get; set; }

    /// <summary>
    /// Gets or sets how many interface members, abstract members and overrides were checked.
    /// </summary>
    public int ImplementationChecks { get; set; }

    /// <summary>
    /// Gets each reference or implementation the server would fail to bind, one line each.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Records a problem.
    /// </summary>
    /// <param name="problem">One line saying what does not bind and where.</param>
    public void Add(string problem) => _problems.Add(problem);
}
