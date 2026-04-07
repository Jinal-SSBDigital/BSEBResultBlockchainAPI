namespace BSEBResultBlockchainAPI.Models
{
    public class StudentResult
    {
        public int Status { get; set; }
        public int IsCCEMarks { get; set; }
        public string? RollCode { get; set; }
        public string? RollNo { get; set; }
        public string? BsebUniqueID { get; set; }
        //public string? msg { get; set; }
        public DateTime? dob { get; set; }
        public string? SN { get; set; }
        public string? FN { get; set; }
        public string? MN { get; set; }
        public string? clgname { get; set; }
        public string? regno { get; set; }
        public string? Faculty { get; set; }
        public string? TotalAggMarks { get; set; }
        public string? TotalAggWords { get; set; }
        public string? Division { get; set; }

        public List<SubjectResult> SubjectResults { get; set; } = new();
    }
}
