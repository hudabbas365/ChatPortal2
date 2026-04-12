using ChatPortal2.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Services;

public class WorkspacePermissionService : IWorkspacePermissionService
{
    private readonly AppDbContext _db;

    public WorkspacePermissionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetRoleAsync(int workspaceId, string userId)
    {
        var workspace = await _db.Workspaces.FindAsync(workspaceId);
        if (workspace == null) return "Viewer";

        if (workspace.OwnerId == userId) return "Admin";

        var wu = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId && w.UserId == userId);

        return wu?.Role ?? "Viewer";
    }

    public async Task<string> GetRoleByGuidAsync(string workspaceGuid, string userId)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspaceGuid);
        if (workspace == null) return "Viewer";

        if (workspace.OwnerId == userId) return "Admin";

        var wu = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspace.Id && w.UserId == userId);

        return wu?.Role ?? "Viewer";
    }

    public async Task<bool> CanViewAsync(int workspaceId, string userId)
    {
        var role = await GetRoleAsync(workspaceId, userId);
        return role == "Admin" || role == "Editor" || role == "Viewer";
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
}
