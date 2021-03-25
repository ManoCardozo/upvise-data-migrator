namespace UpviseDataMigrator.Models
{
    public class Document
    {
        public int DocumentId { get; set; }
        public string ContentType { get; set; }
        public string OriginalFilename { get; set; }
        public byte[] DocumentImage { get; set; }
        public string UpviseContactID { get; set; }
        public string ContactName { get; set; }
        public string BirthDate { get; set; }
        public string Surname { get; set; }
        public string DocumentType { get; set; }
        public bool Uploaded { get; set; }

        public bool IsContactPhoto => DocumentType.Trim() == "Passport Photo";
    }
}
