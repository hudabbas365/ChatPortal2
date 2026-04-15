using AIInsights.Data;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public class WorkspacePermissionService : IWorkspacePermissionService
{
    private readonly AppDbContext _db;

    public WorkspacePermissionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetRoleAsync(int workspaceId, string userId)
    {
        var workspace = await _db.Workspaces.FindAsync(workspaceId);
        if (workspace == null) return null;

        if (workspace.OwnerId == userId) return "Admin";

        var wu = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId && w.UserId == userId);

        return wu?.Role;
    }

    public async Task<string?> GetRoleByGuidAsync(string workspaceGuid, string userId)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspaceGuid);
        if (workspace == null) return null;

        if (workspace.OwnerId == userId) return "Admin";

        var wu = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspace.Id && w.UserId == userId);

        return wu?.Role;
    }

    public async Task<bool> CanViewAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        return role != null;
    }

    public async Task<bool> CanEditAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        return role == "Admin" || role == "Editor";
    }

    public async Task<bool> CanDeleteAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        return role == "Admin";
    }

    public async Task<bool> CanViewAgentsAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        return role == "Admin" || role == "Editor";
    }

    public async Task<bool> CanViewReportsAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        // Viewer, Editor, and Admin can all view reports
        return role == "Admin" || role == "Editor" || role == "Viewer";
    }

    public async Task<bool> BelongsToSameOrganizationAsync(int workspaceId, int organizationId)
    {
        var workspace = await _db.Workspaces.FindAsync(workspaceId);
        return workspace?.OrganizationId == organizationId;
    }
}
