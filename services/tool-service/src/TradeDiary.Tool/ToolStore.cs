using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

static class ToolStore
{
    internal static bool TryUser(HttpRequest request, out Guid userId) => Guid.TryParse(request.HttpContext.User.FindFirst("sub")?.Value, out userId);

    internal static async Task<IReadOnlyList<PresetResponse>> Presets(NpgsqlDataSource db, Guid userId)
    {
        await using var command=db.CreateCommand("SELECT id,name,tool_type,inputs::text,currency,last_used_at,created_at,updated_at FROM tool.presets WHERE user_id=$1 ORDER BY coalesce(last_used_at,updated_at) DESC");command.Parameters.AddWithValue(userId);
        await using var reader=await command.ExecuteReaderAsync();var items=new List<PresetResponse>();while(await reader.ReadAsync())items.Add(ReadPreset(reader));return items;
    }
    internal static async Task<PresetResponse?> CreatePreset(NpgsqlDataSource db,Guid userId,PresetWrite x)
    {
        await using var command=db.CreateCommand("INSERT INTO tool.presets(id,user_id,name,tool_type,inputs,currency) VALUES($1,$2,$3,$4,$5::jsonb,$6) RETURNING id,name,tool_type,inputs::text,currency,last_used_at,created_at,updated_at");command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(userId);command.Parameters.AddWithValue(x.Name.Trim());command.Parameters.AddWithValue(x.ToolType);command.Parameters.AddWithValue(x.Inputs.GetRawText());Text(command,x.Currency?.ToUpperInvariant());
        try { await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();return ReadPreset(reader); } catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation){return null;}
    }
    internal static async Task<int> UpdatePreset(NpgsqlDataSource db,Guid userId,Guid id,PresetWrite x)
    {
        await using var command=db.CreateCommand("UPDATE tool.presets SET name=$3,tool_type=$4,inputs=$5::jsonb,currency=$6,updated_at=now() WHERE id=$1 AND user_id=$2");command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(userId);command.Parameters.AddWithValue(x.Name.Trim());command.Parameters.AddWithValue(x.ToolType);command.Parameters.AddWithValue(x.Inputs.GetRawText());Text(command,x.Currency?.ToUpperInvariant());
        try{return await command.ExecuteNonQueryAsync();}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.UniqueViolation){return -1;}
    }
    internal static async Task<int> DeletePreset(NpgsqlDataSource db,Guid userId,Guid id){await using var command=db.CreateCommand("DELETE FROM tool.presets WHERE id=$1 AND user_id=$2");command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(userId);return await command.ExecuteNonQueryAsync();}
    internal static async Task<int> UsePreset(NpgsqlDataSource db,Guid userId,Guid id){await using var command=db.CreateCommand("UPDATE tool.presets SET last_used_at=now() WHERE id=$1 AND user_id=$2");command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(userId);return await command.ExecuteNonQueryAsync();}

    /// <summary>
    /// Saves the server-calculated snapshot once per user/idempotency key. A retry returns the
    /// existing row, which lets the UI recover from an uncertain response without duplication.
    /// </summary>
    internal static async Task<(SavedCalculationResponse? Item,bool Duplicate)> Save(NpgsqlDataSource db,Guid userId,SavedCalculationWrite x,object output,string key)
    {
        var outputJson=JsonSerializer.Serialize(output,output.GetType(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var command=db.CreateCommand("INSERT INTO tool.saved_calculations(id,user_id,tool_type,inputs,output,currency,symbol,source_diary_id,source_transaction_id,idempotency_key,note) VALUES($1,$2,$3,$4::jsonb,$5::jsonb,$6,$7,$8,$9,$10,$11) ON CONFLICT(user_id,idempotency_key) DO NOTHING RETURNING id,tool_type,schema_version,inputs::text,output::text,currency,symbol,source_diary_id,source_transaction_id,note,created_at");command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(userId);command.Parameters.AddWithValue(x.ToolType);command.Parameters.AddWithValue(x.Inputs.GetRawText());command.Parameters.AddWithValue(outputJson);command.Parameters.AddWithValue(x.Currency.ToUpperInvariant());Text(command,x.Symbol?.Trim().ToUpperInvariant());Uuid(command,x.SourceDiaryId);Uuid(command,x.SourceTransactionId);command.Parameters.AddWithValue(key);Text(command,x.Note);
        await using(var reader=await command.ExecuteReaderAsync()){if(await reader.ReadAsync())return(ReadSaved(reader),false);}
        await using var existing=db.CreateCommand("SELECT id,tool_type,schema_version,inputs::text,output::text,currency,symbol,source_diary_id,source_transaction_id,note,created_at FROM tool.saved_calculations WHERE user_id=$1 AND idempotency_key=$2");existing.Parameters.AddWithValue(userId);existing.Parameters.AddWithValue(key);await using var existingReader=await existing.ExecuteReaderAsync();await existingReader.ReadAsync();return(ReadSaved(existingReader),true);
    }
    internal static async Task<IReadOnlyList<SavedCalculationResponse>> Recent(NpgsqlDataSource db,Guid userId,int limit){await using var command=db.CreateCommand("SELECT id,tool_type,schema_version,inputs::text,output::text,currency,symbol,source_diary_id,source_transaction_id,note,created_at FROM tool.saved_calculations WHERE user_id=$1 ORDER BY created_at DESC,id DESC LIMIT $2");command.Parameters.AddWithValue(userId);command.Parameters.AddWithValue(limit);await using var reader=await command.ExecuteReaderAsync();var items=new List<SavedCalculationResponse>();while(await reader.ReadAsync())items.Add(ReadSaved(reader));return items;}
    internal static async Task<int> DeleteSaved(NpgsqlDataSource db,Guid userId,Guid id){await using var command=db.CreateCommand("DELETE FROM tool.saved_calculations WHERE id=$1 AND user_id=$2");command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(userId);return await command.ExecuteNonQueryAsync();}

    private static PresetResponse ReadPreset(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetString(2),JsonDocument.Parse(r.GetString(3)).RootElement.Clone(),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetDateTime(5),r.GetDateTime(6),r.GetDateTime(7));
    private static SavedCalculationResponse ReadSaved(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),JsonDocument.Parse(r.GetString(3)).RootElement.Clone(),JsonDocument.Parse(r.GetString(4)).RootElement.Clone(),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetGuid(7),r.IsDBNull(8)?null:r.GetGuid(8),r.IsDBNull(9)?null:r.GetString(9),r.GetDateTime(10));
    private static void Text(NpgsqlCommand c,string? value)=>c.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Text,Value=(object?)value??DBNull.Value});
    private static void Uuid(NpgsqlCommand c,Guid? value)=>c.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Uuid,Value=(object?)value??DBNull.Value});
}
