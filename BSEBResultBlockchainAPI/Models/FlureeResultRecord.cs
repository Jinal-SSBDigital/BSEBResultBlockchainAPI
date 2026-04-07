namespace BSEBResultBlockchainAPI.Models
{
    public class FlureeResultRecord
    {
        public string? FlureeSubjectId { get; set; }

        public string? BsebId { get; set; }
        public string? RollCode { get; set; }
        public string? RollNumber { get; set; }

        /// <summary>
        /// Encrypted version history: ["ENC_v1", "ENC_v2", "ENC_v3"]
        /// </summary>
        public List<string> EncryptedData { get; set; } = new();

        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }
}
