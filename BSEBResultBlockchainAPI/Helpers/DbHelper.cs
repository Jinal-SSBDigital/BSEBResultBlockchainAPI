using BSEBResultBlockchainAPI.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;

namespace BSEBResultBlockchainAPI.Helpers
{
    public class DbHelper
    {
        private readonly AppDBContext _context;
        private readonly string _connectionString;
        public DbHelper(AppDBContext context, IConfiguration configuration)
        {
            _context = context;
            _connectionString = configuration.GetConnectionString("dbcs");
        }

        public async Task<List<(string RollCode, string RollNo)>> GetAllRollCodesAsync()
        {
            try
            {
                var result = new List<(string, string)>();
                using var conn = new SqlConnection(_connectionString);
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "GetAllRollCodesForQR";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 300;

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add((
                        reader["RollCode"].ToString()!,
                        reader["RollNo"].ToString()!
                    ));
                }
                return result;
            }
            catch (Exception ex)
            {

                throw;
            }
           
        }

        public async Task<StudentResult?> GetStudentResultAsync(string rollcode, string rollno)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "FinalResultDataForBlk";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@rollcode", rollcode));
                cmd.Parameters.Add(new SqlParameter("@rollno", rollno));

                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) return null;

                var student = new StudentResult
                {
                    Status = reader.GetInt32(reader.GetOrdinal("status")),
                    IsCCEMarks = reader.GetInt32(reader.GetOrdinal("IsCCEMarks")),
                    RollCode = reader["rollcode"].ToString(),
                    RollNo = reader["rollno"].ToString(),
                    BsebUniqueID = reader["BsebUniqueID"].ToString(),
                    //msg = reader["msg"].ToString(),
                    dob = DateTime.TryParse(reader["dob"]?.ToString(), out var d) ? d : null,
                    SN = reader["SN"].ToString(),
                    FN = reader["FN"].ToString(),
                    MN = reader["MN"].ToString(),
                    clgname = reader["clgname"].ToString(),
                    regno = reader["regno"].ToString(),
                    Faculty = reader["FACULTY"].ToString(),
                    TotalAggMarks = reader["TotalAggMarks"].ToString(),
                    TotalAggWords = reader["TotalAggWords"].ToString(),
                    Division = reader["DIVISION"].ToString()
                };

                while (await reader.NextResultAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        student.SubjectResults.Add(new SubjectResult
                        {
                            Sub = reader["Sub"]?.ToString(),
                            MaxMark = reader.IsDBNull("maxMark") ? null : reader.GetInt32("maxMark"),
                            PassMark = reader.IsDBNull("passMark") ? null : reader.GetInt32("passMark"),
                            Theory = reader["theory"]?.ToString(),
                            OB_PR = reader["OB_PR"]?.ToString(),
                            GRC_THO = reader["GRC_THO"]?.ToString(),
                            GRC_PR = reader["GRC_PR"]?.ToString(),
                            CCEMarks = reader["CCEMarks"]?.ToString(),
                            TotSub = reader["TOT_SUB"]?.ToString(),
                            SubjectGroupName = reader["SubjectGroupName"]?.ToString()
                        });
                    }
                }

                return student;
            }
            catch { throw; }
        }
    }
}