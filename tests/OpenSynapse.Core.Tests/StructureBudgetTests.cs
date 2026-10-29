using System.IO.Enumeration;
using System.Text.Json;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The ratchet: every file and type recorded in .structure-budget.json may only
/// shrink. New files must stay under the default limits. Raising a budget is a
/// deliberate, reviewable edit; growing a file by accident is a failing test.
/// </summary>
public sealed class StructureBudgetTests
{
    public const string BudgetFile = ".structure-budget.json";

    [Fact]
    public void BudgetedFilesStayWithinTheirLineBudget()
    {
        var failures = new List<string>();
        foreach (var entry in LoadBudget().Files)
        {
            var path = Absolute(entry.Path);
            if (!File.Exists(path))
            {
                failures.Add($"{entry.Path} 预算条目已失效，删掉这一行");
                continue;
            }

            var lines = RepoLayout.CountLines(path);
            if (lines > entry.MaxLines)
            {
                failures.Add($"{entry.Path} 超预算：{lines} > {entry.MaxLines}（多了 {lines - entry.MaxLines} 行）");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void BudgetedTypesStayWithinTheirPublicMemberBudget()
    {
        var failures = new List<string>();
        var budget = LoadBudget();
        foreach (var entry in budget.Files.Where(item => item.MaxPublicMembers is not null))
        {
            var path = Absolute(entry.Path);
            if (!File.Exists(path))
            {
                continue;
            }

            var members = RepoLayout.CountPublicMembers(path);
            if (members > entry.MaxPublicMembers!.Value)
            {
                failures.Add($"{entry.Path} public 成员超预算：{members} > {entry.MaxPublicMembers}");
            }
        }

        foreach (var group in budget.Groups.Where(item => item.MaxPublicMembers is not null))
        {
            var members = group.MatchedFiles().Sum(RepoLayout.CountPublicMembers);
            if (members > group.MaxPublicMembers!.Value)
            {
                failures.Add($"{group.Pattern} public 成员超预算：{members} > {group.MaxPublicMembers}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void UnbudgetedFilesStayUnderTheDefaultLimits()
    {
        var budget = LoadBudget();
        var failures = new List<string>();
        foreach (var path in RepoLayout.SourceFiles())
        {
            var relative = RepoLayout.Relative(path);
            if (budget.Covers(relative))
            {
                continue;
            }

            var lines = RepoLayout.CountLines(path);
            if (lines > budget.NewFileLineLimit)
            {
                failures.Add($"{relative} 新文件超过 {budget.NewFileLineLimit} 行（{lines}）：拆开，或按流程登记预算");
            }

            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var members = RepoLayout.CountPublicMembers(path);
                if (members > budget.NewFilePublicMemberLimit)
                {
                    failures.Add($"{relative} 新文件 public 成员超过 {budget.NewFilePublicMemberLimit}（{members}）");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void GroupBudgetsStillMatchSomething()
    {
        var stale = LoadBudget().Groups
            .Where(group => group.MatchedFiles().Count == 0)
            .Select(group => group.Pattern)
            .ToList();
        Assert.True(stale.Count == 0, $"分组预算没有匹配到文件：{string.Join(", ", stale)}");
    }

    internal static string Absolute(string relativePath) =>
        Path.Combine(RepoLayout.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    internal static Budget LoadBudget()
    {
        var path = Absolute(BudgetFile);
        Assert.True(File.Exists(path), $"缺少 {BudgetFile}");
        return JsonSerializer.Deserialize<Budget>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"{BudgetFile} 解析为空");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal sealed class Budget
    {
        public int NewFileLineLimit { get; set; } = 600;

        public int NewFilePublicMemberLimit { get; set; } = 100;

        public List<FileBudget> Files { get; set; } = [];

        public List<GroupBudget> Groups { get; set; } = [];

        public bool Covers(string relativePath) =>
            Files.Any(entry => string.Equals(entry.Path, relativePath, StringComparison.Ordinal)) ||
            Groups.Any(group => group.Matches(relativePath));
    }

    internal sealed class FileBudget
    {
        public string Path { get; set; } = string.Empty;

        public int MaxLines { get; set; } = int.MaxValue;

        public int? MaxPublicMembers { get; set; }
    }

    internal sealed class GroupBudget
    {
        public string Pattern { get; set; } = string.Empty;

        public int MaxLines { get; set; } = int.MaxValue;

        public int? MaxPublicMembers { get; set; }

        public bool Matches(string relativePath)
        {
            var slash = Pattern.LastIndexOf('/');
            var folder = slash < 0 ? string.Empty : Pattern[..slash];
            var mask = slash < 0 ? Pattern : Pattern[(slash + 1)..];
            var prefix = folder + "/";
            return relativePath.StartsWith(prefix, StringComparison.Ordinal) &&
                FileSystemName.MatchesSimpleExpression(mask, relativePath.AsSpan(prefix.Length), ignoreCase: false);
        }

        public IReadOnlyList<string> MatchedFiles() =>
            RepoLayout.SourceFiles().Where(path => Matches(RepoLayout.Relative(path))).ToList();
    }
}
