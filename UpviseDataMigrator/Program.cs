using System;
using System.Linq;
using System.Threading;
using System.Configuration;
using System.Data.SqlClient;
using System.Collections.Generic;
using UpviseDataMigrator.Models;
using com.upvise.client;

namespace UpviseDataMigrator
{
    class Program
    {
        private static int _totalFileCount = 0;
        private static int _totalFilesUploaded = 0;

        static void Main(string[] args)
        {
            var option = ReadMenuOption();

            if (option == "1")
            {
                var validRange = ReadContactIdRange(out int fromContactId, out int toContactId);

                if (validRange)
                {
                    var documents = GetDocuments(fromContactId, toContactId);
                    _totalFileCount = documents.Count;
                    var contactCount = documents.GroupBy(x => x.UpviseContactID).Count();
                    Console.WriteLine();

                    if (_totalFileCount > 0)
                    {
                        Console.WriteLine("Query results");
                        Console.WriteLine($"Total Contacts: {contactCount}");
                        Console.WriteLine($"Total Attachments: {_totalFileCount}");
                        Console.WriteLine($"Continue upload to Upvise? y/n");
                        var shouldUpload = Console.ReadLine() == "y";

                        if (shouldUpload)
                        {
                            UploadToUpvise(documents);
                            Console.WriteLine($"Total number of files uploaded to Upvise: {_totalFilesUploaded}");
                        }
                    }
                    else
                    {
                        ShowErrorMessage("No records matched the specified Contact ID range.");
                    }
                }
            }

            NotifyEndOfProgram();
        }

        private static void ShowSuccessMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void ShowErrorMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static string ReadMenuOption()
        {
            Console.WriteLine("Upvise Migrator");
            Console.WriteLine("1 - Migrate attachments for contacts");
            Console.WriteLine("2 - Exit");
            return Console.ReadLine();
        }

        private static bool ReadContactIdRange(out int fromContactId, out int toContactId)
        {
            try
            {
                Console.WriteLine();

                Console.WriteLine("From Contact ID: ");
                fromContactId = Convert.ToInt32(Console.ReadLine());

                Console.WriteLine("To Contact ID: ");
                toContactId = Convert.ToInt32(Console.ReadLine());

                return true;
            }
            catch (Exception)
            {
                fromContactId = 0;
                toContactId = 0;

                Console.WriteLine();
                ShowErrorMessage("Invalid Contact ID");

                return false;
            }
        }

        private static void UploadToUpvise(List<Document> documents)
        {
            var query = LoginToUpvise();

            var contactIds = documents
                .GroupBy(x => x.UpviseContactID)
                .Select(x => x.Key)
                .ToList();

            foreach (var contactId in contactIds)
            {
                var contact = query.selectId(UpviseConstants.Table.Contacts, contactId);
                if (contact != null)
                {
                    var contactAttachments = documents
                        .Where(x => x.UpviseContactID == contactId);

                    Console.WriteLine($"Checking existing attachments for Contact ID {contactId}");
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    var existingAttachments = query.select(UpviseConstants.Table.Files, $"linkedtable='{UpviseConstants.Table.Contacts}' AND linkedrecid='{contactId}'");

                    foreach (var contactAttachment in contactAttachments)
                    {
                        var fileId = contactAttachment.DocumentId.ToString();

                        try
                        {
                            var fileAlreadyExists = existingAttachments
                                .Any(x => x.getString("id") == fileId);

                            if (!fileAlreadyExists)
                            {
                                CreateAttachment(query, contactId, contactAttachment, fileId);

                                if (contactAttachment.IsContactPhoto)
                                {
                                    TurnAttachmentToContactPhoto(query, contactId, fileId);
                                }

                                MarkAsUploaded(fileId);
                                _totalFilesUploaded++;
                                ShowSuccessMessage($"Progress: {_totalFilesUploaded} out of {_totalFileCount} done");
                            }
                            else
                            {
                                ShowErrorMessage($"Skipped - Attachment with document ID {fileId} already exists");
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowErrorMessage($"Error while updaing Document ID {fileId}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    ShowErrorMessage($"Upvise contact with ID {contactId} was not found.");
                }
            }
        }

        private static void TurnAttachmentToContactPhoto(Query query, string contactId, string fileId)
        {
            Console.WriteLine($"Attachment with document ID {fileId} is a Contact Photo");
            Thread.Sleep(TimeSpan.FromSeconds(0.5));
            var contactObj = new JSONObject();
            contactObj.put("photoid", fileId);
            query.updateId(UpviseConstants.Table.Contacts, contactId, contactObj);

            Thread.Sleep(TimeSpan.FromSeconds(0.5));
            var fileObj = new JSONObject();
            fileObj.put("folderid", "system");
            fileObj.put("linkedtable", "");
            fileObj.put("linkedrecid", "");
            query.updateId(UpviseConstants.Table.Files, fileId, fileObj);

            Console.WriteLine($"Uploaded - Contact Photo with document ID {fileId}");
        }

        private static void CreateAttachment(Query query, string contactId, Document contactAttachment, string fileId)
        {
            var file = new File
            {
                id = fileId,
                name = contactAttachment.OriginalFilename,
                mime = contactAttachment.ContentType,
                linkedtable = UpviseConstants.Table.Contacts,
                linkedid = contactId
            };

            Console.WriteLine($"Uploading - Attachment with Document ID {fileId}");
            Thread.Sleep(TimeSpan.FromSeconds(0.5));
            query.uploadFile(file, contactAttachment.DocumentImage);
            Console.WriteLine($"Uploaded - Document ID {fileId}");
        }

        private static Query LoginToUpvise()
        {
            Console.WriteLine("Connecting to Upvise...");

            var upviseLogin = ConfigurationManager.AppSettings["upviseLogin"];
            var upvisePassword = ConfigurationManager.AppSettings["upvisePassword"];

            var result = Query.login(upviseLogin, upvisePassword);

            Console.WriteLine("Connection opened.");

            return result;
        }

        private static SqlConnection GetDatabaseConnection()
        {
            try
            {
                string datasource = ConfigurationManager.AppSettings["datasource"];
                string database = ConfigurationManager.AppSettings["database"];
                string connectionString = $"Data Source={datasource};Initial Catalog={database};Integrated Security=true";
                var connection = new SqlConnection(connectionString);
                return connection;
            }
            catch (Exception)
            {
                ShowErrorMessage("Unable to open database connection");
                throw;
            }
        }

        private static List<Document> GetDocuments(int fromContactId, int toContactId)
        {
            Console.WriteLine();
            Console.WriteLine("Connecting to database...");

            var connection = GetDatabaseConnection();

            var documents = new List<Document>();

            try
            {
                connection.Open();

                Console.WriteLine("Connection opened.");
                Console.WriteLine("Running query against the database...");

                var query = $@"SELECT 
                                    DocumentID, 
                                    ContentType, 
                                    OriginalFilename,
                                    DocumentImage,
                                    UpviseContactID,
                                    ContactName,
                                    BirthDate,
                                    Surname,
                                    DocumentType,
                                    Uploaded
                                FROM Temp_DocumentsExport 
                                WHERE Uploaded = 0
                                AND UpviseContactID >= {fromContactId} 
                                AND UpviseContactID <= {toContactId}";

                using (var command = new SqlCommand(query, connection))
                {
                    var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        documents.Add(new Document
                        {
                            DocumentId = Convert.ToInt32(reader["DocumentID"].ToString()),
                            ContentType = reader["ContentType"].ToString(),
                            OriginalFilename = reader["OriginalFilename"].ToString(),
                            DocumentImage = (byte[])reader["DocumentImage"],
                            UpviseContactID = reader["UpviseContactID"].ToString(),
                            ContactName = reader["ContactName"].ToString(),
                            BirthDate = reader["BirthDate"].ToString(),
                            Surname = reader["Surname"].ToString(),
                            DocumentType = reader["DocumentType"].ToString(),
                            Uploaded = (bool)reader["Uploaded"]
                        });
                    }
                }

                connection.Close();
                connection.Dispose();
            }
            catch (Exception e)
            {
                ShowErrorMessage("Error while accessing the database: " + e.Message);
                throw;
            }

            return documents;
        }

        private static void MarkAsUploaded(string documentId)
        {
            var connection = GetDatabaseConnection();

            try
            {
                connection.Open();

                Console.WriteLine("Marking document as uploaded.");

                var query = $@"UPDATE Temp_DocumentsExport 
                                set Uploaded = 1
                                WHERE DocumentID = {documentId}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.ExecuteNonQuery();
                }

                connection.Close();
                connection.Dispose();
            }
            catch (Exception e)
            {
                ShowErrorMessage("Error while accessing the database to mark document as uploaded: " + e.Message);
                throw;
            }
        }

        private static void NotifyEndOfProgram()
        {
            Console.WriteLine();
            Console.WriteLine("Program ended");
            Console.WriteLine("Press any key to continue...");
            Console.ReadLine();
        }
    }
}
