namespace BSEBResultBlockchainAPI.Models
{
    public class FlureeResultRecord
    {
     
        public string? BsebId { get; set; }
        public string? RollCode { get; set; }
        public string? RollNumber { get; set; }
        //public string? enc_v1 { get; set; }
        //public string? enc_v2 { get; set; }
        public string? approval1 { get; set; }
        public string? approval2 { get; set; }

        /// <summary>
        /// Encrypted version history: ["ENC_v1", "ENC_v2", "ENC_v3"]
        /// </summary>
        //public List<string> EncryptedData { get; set; } = new();
        public List<Dictionary<string, string>> EncryptedData { get; set; } = new();
        public List<Dictionary<string, string>> EncryptedData_EncV1 { get; set; } = new();
        public List<Dictionary<string, string>> enc_v1 { get; set; } = new();
        public List<Dictionary<string, string>> enc_v2 { get; set; } = new();


        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
        public string? FlureeSubjectId { get; set; }

    }
}
