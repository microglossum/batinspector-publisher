namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Persistence for the iNaturalist login. Implement it to keep tokens somewhere else (e.g. a host-managed vault).</summary>
public interface INaturalistTokenStore
{
    /// <summary>
    /// False if <see cref="Save"/> is known to fail on this store. The authenticator checks it before it opens the browser, so a user does not authorize a login that cannot be kept.
    /// Defaults to true.
    /// </summary>
    bool CanSave => true;

    /// <summary>Returns the stored token or null if there is none. A stored login that cannot be read counts as "no token".</summary>
    INaturalistToken? Load();

    /// <summary>Stores the token, replacing any previous one.</summary>
    void Save(INaturalistToken token);

    /// <summary>Removes the stored token.</summary>
    void Clear();
}
