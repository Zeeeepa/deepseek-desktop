using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>
/// 多账户负载均衡（对齐 DSD API <c>src/main/proxy/loadbalancer.ts</c>：round-robin / fill-first / failover）。
/// </summary>
public sealed class AccountLoadBalancer
{
    public const string StrategyRoundRobin = "round-robin";
    public const string StrategyFillFirst = "fill-first";
    public const string StrategyFailover = "failover";

    private const int FailThreshold = 3;
    private const long RecoveryTimeMs = 60_000;

    private static readonly AccountLoadBalancer Shared = new();
    public static AccountLoadBalancer Instance => Shared;

    public void ResetStateForTests()
    {
        lock (_gate)
        {
            _roundRobinIndex.Clear();
            _failedAccounts.Clear();
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _roundRobinIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccountFailureState> _failedAccounts = new(StringComparer.Ordinal);

    private sealed class AccountFailureState
    {
        public int Count;
        public long LastFailTime;
    }

    public void MarkAccountFailed(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return;
        lock (_gate)
        {
            if (!_failedAccounts.TryGetValue(accountId, out var state))
                state = new AccountFailureState();
            state.Count++;
            state.LastFailTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _failedAccounts[accountId] = state;
        }
    }

    public void ClearAccountFailure(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return;
        lock (_gate)
        {
            _failedAccounts.Remove(accountId);
        }
    }

    public AccountRouteSelection? SelectAccount(
        AppConfig config,
        string model,
        string? preferredProviderId = null,
        string? preferredAccountId = null)
    {
        var strategy = NormalizeStrategy(config.DsdApiLoadBalanceStrategy);
        var excludeFailed = string.Equals(strategy, StrategyFailover, StringComparison.OrdinalIgnoreCase);
        var candidates = GetAvailableAccounts(config, model, preferredProviderId, excludeFailed);
        if (candidates.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredAccountId))
        {
            var preferred = candidates.FirstOrDefault(c =>
                string.Equals(c.Account.Id, preferredAccountId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null && !IsAccountInFailure(preferred.Account.Id))
                return preferred;
        }

        return strategy switch
        {
            StrategyFillFirst => SelectFillFirst(candidates),
            StrategyFailover => SelectFailover(candidates),
            _ => SelectRoundRobin(candidates)
        };
    }

    private static string NormalizeStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            return StrategyRoundRobin;
        return strategy.Trim().ToLowerInvariant() switch
        {
            "fill-first" or "fillfirst" => StrategyFillFirst,
            "failover" => StrategyFailover,
            _ => StrategyRoundRobin
        };
    }

    private List<AccountRouteSelection> GetAvailableAccounts(
        AppConfig config,
        string model,
        string? preferredProviderId,
        bool excludeFailed)
    {
        var candidates = new List<AccountRouteSelection>();
        foreach (var provider in ApiProviderRegistry.LoadAll(config).Where(p => p.Enabled))
        {
            if (!string.IsNullOrWhiteSpace(preferredProviderId) &&
                !string.Equals(provider.Id, preferredProviderId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ProviderSupportsModel(config, provider, model))
                continue;

            foreach (var account in ProviderAccountStore.ByProvider(provider.Id))
            {
                if (!IsAccountAvailable(account))
                    continue;
                if (excludeFailed && IsAccountInFailure(account.Id))
                    continue;

                candidates.Add(new AccountRouteSelection
                {
                    Provider = provider,
                    Account = account,
                    ActualModel = MapModel(config, provider, model)
                });
            }
        }

        return candidates;
    }

    private static bool ProviderSupportsModel(AppConfig config, ApiProviderEntry provider, string model)
    {
        if (string.Equals(provider.Id, "deepseek", StringComparison.OrdinalIgnoreCase)
            && DsdOpenAiCompat.ListModelIds(config).Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var models = ResolveProviderModels(config, provider);
        if (models.Count == 0)
            return true;

        var normalized = model.ToLowerInvariant();
        foreach (var m in models)
        {
            var supported = m.ToLowerInvariant();
            if (supported.EndsWith('*'))
            {
                if (normalized.StartsWith(supported[..^1], StringComparison.Ordinal))
                    return true;
            }
            else if (string.Equals(supported, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var mapping = config.ModelMappings.FirstOrDefault(x =>
            x.RequestModel.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (mapping is null)
            return false;

        if (!string.IsNullOrWhiteSpace(mapping.PreferredProviderId) &&
            !string.Equals(mapping.PreferredProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var actual = string.IsNullOrWhiteSpace(mapping.ActualModel) ? model : mapping.ActualModel;
        var normalizedActual = actual.ToLowerInvariant();
        return models.Any(m =>
        {
            var supported = m.ToLowerInvariant();
            if (supported.EndsWith('*'))
                return normalizedActual.StartsWith(supported[..^1], StringComparison.Ordinal);
            return string.Equals(supported, normalizedActual, StringComparison.Ordinal);
        });
    }

    private static bool IsAccountAvailable(ProviderAccountRecord account)
    {
        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        if (account.DailyLimit is > 0 && account.TodayUsed >= account.DailyLimit)
            return false;

        return account.Credentials.Values.Any(v => !string.IsNullOrWhiteSpace(v))
               || string.Equals(account.ProviderId, "deepseek", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapModel(AppConfig config, ApiProviderEntry provider, string model)
    {
        var models = ResolveProviderModels(config, provider);
        var exact = models.FirstOrDefault(m =>
            m.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return model;

        var mapping = config.ModelMappings.FirstOrDefault(x =>
            x.RequestModel.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (mapping is not null &&
            (string.IsNullOrWhiteSpace(mapping.PreferredProviderId) ||
             string.Equals(mapping.PreferredProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(mapping.ActualModel))
        {
            return mapping.ActualModel;
        }

        return DsdOpenAiCompat.MapModel(model, config);
    }

    private static IReadOnlyList<string> ResolveProviderModels(AppConfig config, ApiProviderEntry provider)
    {
        if (provider.Models.Count > 0)
            return provider.Models;

        var builtin = BuiltinProviderCatalog.Find(provider.Id)?.Models;
        if (builtin is { Length: > 0 })
            return builtin;

        if (string.Equals(provider.Id, "deepseek", StringComparison.OrdinalIgnoreCase))
            return DsdOpenAiCompat.ListModelIds(config).ToArray();

        return Array.Empty<string>();
    }

    private AccountRouteSelection SelectRoundRobin(List<AccountRouteSelection> candidates)
    {
        var key = string.Join(',', candidates.Select(c => c.Provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Order());
        lock (_gate)
        {
            var index = _roundRobinIndex.GetValueOrDefault(key);
            var selected = candidates[index % candidates.Count];
            _roundRobinIndex[key] = (index + 1) % candidates.Count;
            return selected;
        }
    }

    private static AccountRouteSelection SelectFillFirst(List<AccountRouteSelection> candidates) =>
        candidates.Aggregate((best, current) =>
        {
            var bestUsed = best.Account.TodayUsed;
            var currentUsed = current.Account.TodayUsed;
            if (currentUsed < bestUsed)
                return current;
            if (currentUsed == bestUsed && current.Account.LastUsed < best.Account.LastUsed)
                return current;
            return best;
        });

    private AccountRouteSelection SelectFailover(List<AccountRouteSelection> candidates)
    {
        var healthy = candidates.Where(c => !IsAccountInFailure(c.Account.Id)).ToList();
        if (healthy.Count > 0)
            return SelectRoundRobin(healthy);

        var sorted = candidates
            .OrderBy(c => _failedAccounts.TryGetValue(c.Account.Id, out var f) ? f.Count : 0)
            .ThenBy(c => _failedAccounts.TryGetValue(c.Account.Id, out var f) ? f.LastFailTime : 0)
            .ToList();
        return sorted[0];
    }

    private bool IsAccountInFailure(string accountId)
    {
        lock (_gate)
        {
            if (!_failedAccounts.TryGetValue(accountId, out var failure))
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - failure.LastFailTime > RecoveryTimeMs)
            {
                _failedAccounts.Remove(accountId);
                return false;
            }

            return failure.Count >= FailThreshold;
        }
    }
}
