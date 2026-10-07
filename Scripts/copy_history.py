with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Bbs\Index.cshtml', 'r', encoding='utf-8') as f:
    c = f.read()

# Update title and links
c = c.replace('action="/Bbs/Index"', 'action="/Bbs/History"')
c = c.replace('asp-action="Create" asp-route-type="Rutin"', 'asp-action="Index"')
c = c.replace('<span>Observasi Rutin</span>', '<span>Input Observasi BBS</span>')
c = c.replace('Riwayat Observasi', 'Riwayat Data Observasi')

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Bbs\History.cshtml', 'w', encoding='utf-8') as f:
    f.write(c)

print('History.cshtml created successfully!')
