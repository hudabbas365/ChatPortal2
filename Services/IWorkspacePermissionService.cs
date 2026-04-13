namespace ChatPortal2.Services;

public interface IWorkspacePermissionService
{
    Task<string?> GetRoleAsync(int workspaceId, string userId);
    Task<string?> GetRoleByGuidAsync(string workspaceGuid, string userId);
    Task<bool> CanViewAsync(int workspaceId, string userId);
    Task<bool> CanEditAsync(int workspaceId, string userId);
    Task<bool> CanDeleteAsync(int workspaceId, string userId);
    Task<bool> CanViewAgentsAsync(int workspaceId, string userId);
}
