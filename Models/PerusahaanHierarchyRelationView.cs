namespace MBS_SAP.Models
{
    // Keyless projection for DB_SAP.dbo.vw_m_hirarki_perusahaan
    public class PerusahaanHierarchyRelationView
    {
        public string? RowKind { get; set; }
        public int? RelasiId { get; set; }

        public int? ParentCompanyId { get; set; }
        public string? ParentCompanyCode { get; set; }
        public string? ParentCompanyName { get; set; }
        public bool? ParentIsActive { get; set; }
        public string? ParentStatusPerusahaan { get; set; }
        public int? ParentCompanyTypeId { get; set; }
        public string? ParentCompanyTypeName { get; set; }

        public int? ChildCompanyId { get; set; }
        public string? ChildCompanyCode { get; set; }
        public string? ChildCompanyName { get; set; }
        public bool? ChildIsActive { get; set; }
        public string? ChildStatusPerusahaan { get; set; }
        public int? ChildCompanyTypeId { get; set; }
        public string? ChildCompanyTypeName { get; set; }

        public int? RelationRoleId { get; set; }
        public string? RelationRoleName { get; set; }
        public string? EffectiveRoleName { get; set; }
        public int? EffectiveRoleOrder { get; set; }
        public bool? IsRoot { get; set; }
        public bool? IsLeaf { get; set; }
        public DateTime? RelationCreatedAt { get; set; }
        public DateTime? RelationUpdatedAt { get; set; }
    }
}
