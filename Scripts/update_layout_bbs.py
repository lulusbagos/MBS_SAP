with open('Views/Shared/_Layout.cshtml', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add BBS to Sidebar
sidebar_obs = """                    <a asp-controller="Observation" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "Observation" ? "active" : "")">
                        <i class="bi bi-eye-fill"></i>
                        <span>Observation</span>
                    </a>"""

sidebar_bbs = """                    <a asp-controller="Observation" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "Observation" ? "active" : "")">
                        <i class="bi bi-eye-fill"></i>
                        <span>Observation</span>
                    </a>
                    <a asp-controller="Bbs" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "BBS" ? "active" : "")">
                        <i class="bi bi-shield-check" style="color: #38bdf8;"></i>
                        <span>BBS (Perilaku)</span>
                    </a>"""

if sidebar_obs in content:
    content = content.replace(sidebar_obs, sidebar_bbs, 1)
    print("BBS sidebar menu added.")
else:
    print("Observation menu not found for replacement.")

# 2. Add BBS Master to Admin menu
admin_user_mgmt = """                        <a asp-controller="UserManagement" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "UserMgmt" ? "active" : "")">
                            <i class="bi bi-person-gear" style="color: #a855f7;"></i>
                            <span>User Management</span>
                        </a>"""

admin_bbs_master = """                        <a asp-controller="UserManagement" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "UserMgmt" ? "active" : "")">
                            <i class="bi bi-person-gear" style="color: #a855f7;"></i>
                            <span>User Management</span>
                        </a>
                        <a asp-controller="BbsMaster" asp-action="Index" class="menu-item @(ViewData["ActiveTab"]?.ToString() == "BbsMaster" ? "active" : "")">
                            <i class="bi bi-card-checklist" style="color: #38bdf8;"></i>
                            <span>Master Data BBS</span>
                        </a>"""

if admin_user_mgmt in content:
    content = content.replace(admin_user_mgmt, admin_bbs_master, 1)
    print("BBS Master menu added.")
else:
    print("UserManagement menu not found for replacement.")

with open('Views/Shared/_Layout.cshtml', 'w', encoding='utf-8') as f:
    f.write(content)
print("Updated _Layout.cshtml successfully!")
