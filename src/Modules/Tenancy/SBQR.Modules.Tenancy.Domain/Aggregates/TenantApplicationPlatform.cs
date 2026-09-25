namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Mobile platform that owns a <see cref="TenantApplication"/>. The DB CHECK
/// constraint on <c>tenant_applications.platform</c> allows only
/// <see cref="Android"/> / <see cref="Ios"/>; the C# enum maps 1:1 to those
/// UPPER_SNAKE_CASE values.
/// </summary>
public enum TenantApplicationPlatform
{
    /// <summary>Android — the row's <c>package_id</c> is the Google Play <c>applicationId</c>.</summary>
    Android = 0,

    /// <summary>iOS — the row's <c>package_id</c> is the App Store bundle identifier.</summary>
    Ios = 1,
}
