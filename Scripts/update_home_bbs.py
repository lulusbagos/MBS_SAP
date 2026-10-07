with open('Views/Home/Index.cshtml', 'r', encoding='utf-8') as f:
    content = f.read()

target = """            <a asp-controller="Coaching" asp-action="Index" class="btn-action-card animate-pop-in delay-6">
                <i class="bi bi-person-video3" style="font-size: 32px; color: #3b82f6; display: block; margin: 0 auto 12px; text-align: center;"></i>
                <span style="font-weight: 700; font-size: 13px;">Coaching</span>
                <span class="badge" style="background-color: rgba(59, 130, 246, 0.1); color: #3b82f6; font-size: 10px; font-weight: 600;">Akt: @(ViewData["ThisMonthCoachings"] ?? "0") / Tgt: @(ViewData["TargetCoaching"] ?? "0")</span>
            </a>"""

replacement = """            <a asp-controller="Coaching" asp-action="Index" class="btn-action-card animate-pop-in delay-6">
                <i class="bi bi-person-video3" style="font-size: 32px; color: #3b82f6; display: block; margin: 0 auto 12px; text-align: center;"></i>
                <span style="font-weight: 700; font-size: 13px;">Coaching</span>
                <span class="badge" style="background-color: rgba(59, 130, 246, 0.1); color: #3b82f6; font-size: 10px; font-weight: 600;">Akt: @(ViewData["ThisMonthCoachings"] ?? "0") / Tgt: @(ViewData["TargetCoaching"] ?? "0")</span>
            </a>

            <a asp-controller="Bbs" asp-action="Index" class="btn-action-card animate-pop-in delay-6">
                <div style="width: 44px; height: 44px; border-radius: 12px; background: linear-gradient(135deg, #0284c7, #38bdf8); display: flex; align-items: center; justify-content: center; margin: 0 auto 10px;">
                    <i class="bi bi-shield-check" style="font-size: 22px; color: white;"></i>
                </div>
                <span style="font-weight: 700; font-size: 13px;">BBS Perilaku</span>
                <span class="badge" style="background-color: rgba(2, 132, 199, 0.12); color: #0284c7; font-size: 10px; font-weight: 600;">Observasi K3</span>
            </a>"""

if target in content:
    content = content.replace(target, replacement, 1)
    print("BBS card added to Home action-grid.")
else:
    print("Target coaching block not found.")

with open('Views/Home/Index.cshtml', 'w', encoding='utf-8') as f:
    f.write(content)
print("Updated Views/Home/Index.cshtml successfully!")
