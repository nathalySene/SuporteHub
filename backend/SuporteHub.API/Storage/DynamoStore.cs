using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Globalization;
using System.Text.Json;
using SuporteHub.API.Models;

namespace SuporteHub.API.Storage;

public class DynamoStore(IAmazonDynamoDB db, IConfiguration config) : IAppStore
{
    public string ProviderName => "DynamoDB";
    public string Table => config["DynamoDb:TableName"] ?? throw new InvalidOperationException("Configure DynamoDb:TableName.");
    static Dictionary<string, AttributeValue> Key(string pk, string sk) => new() { ["PK"] = new(pk), ["SK"] = new(sk) };

    public bool IsLocal => !string.IsNullOrEmpty(config["DynamoDb:ServiceUrl"]);

    public async Task<bool> NeedsInitialAgent() => IsLocal
        && await Get<object>("META", "INITIAL_AGENT") is null
        && (await Query<Conta>("USERS")).Count == 0;

    public async Task Register(Conta account)
    {
        if (!await NeedsInitialAgent())
        {
            await Put("USERS", account.Email, account);
            return;
        }
        account.Perfil = "agent";
        var item = Key("USERS", account.Email);
        item["Data"] = new(JsonSerializer.Serialize(account));
        item["Version"] = new() { N = "1" };
        var marker = Key("META", "INITIAL_AGENT");
        marker["Data"] = new("{}"); marker["Version"] = new() { N = "1" };
        // Exactly one first local registration can claim the support role.
        try { await db.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [
                new() { Put = new() { TableName = Table, Item = marker, ConditionExpression = "attribute_not_exists(PK)" } },
                new() { Put = new() { TableName = Table, Item = item, ConditionExpression = "attribute_not_exists(PK)" } }
            ]
        }); }
        catch (TransactionCanceledException e)
        { throw new StorageConflictException("O cadastro inicial foi atualizado. Tente novamente para criar sua conta.", e); }
    }

    public async Task InitializeAsync()
    {
        // Table creation is restricted to explicitly configured local endpoints.
        if (!string.IsNullOrEmpty(config["DynamoDb:ServiceUrl"]))
        {
            try { await db.DescribeTableAsync(Table); }
            catch (ResourceNotFoundException)
            {
                await db.CreateTableAsync(new CreateTableRequest
                {
                    TableName = Table, BillingMode = BillingMode.PAY_PER_REQUEST,
                    KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)],
                    AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)]
                });
            }
        }
        // Read-only check; never creates or migrates an AWS table.
        await db.GetItemAsync(new GetItemRequest { TableName = Table, Key = Key("META", "READINESS"), ConsistentRead = true });
    }

    public async Task<(T Value, int Version)?> Get<T>(string pk, string sk)
    {
        var r = await db.GetItemAsync(new GetItemRequest { TableName = Table, Key = Key(pk, sk), ConsistentRead = true });
        if (r.Item is null || r.Item.Count == 0) return null;
        return (JsonSerializer.Deserialize<T>(r.Item["Data"].S)!, int.Parse(r.Item["Version"].N, CultureInfo.InvariantCulture));
    }

    public async Task<List<T>> Query<T>(string pk)
    {
        List<T> rows = [];
        Dictionary<string, AttributeValue>? last = null;
        do
        {
            var r = await db.QueryAsync(new QueryRequest
            {
                TableName = Table, KeyConditionExpression = "PK = :pk",
                ExpressionAttributeValues = new() { [":pk"] = new(pk) },
                ConsistentRead = true, ExclusiveStartKey = last
            });
            rows.AddRange(r.Items.Select(i => JsonSerializer.Deserialize<T>(i["Data"].S)!));
            last = r.LastEvaluatedKey;
        } while (last is { Count: > 0 });
        return rows;
    }

    public async Task Put<T>(string pk, string sk, T value, int? previousVersion = null)
    {
        var json = JsonSerializer.Serialize(value);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 300_000)
            throw new InvalidOperationException("Este chamado atingiu o limite de histórico e anexos.");
        var item = Key(pk, sk);
        item["Data"] = new(json);
        item["Version"] = new() { N = ((previousVersion ?? 0) + 1).ToString(CultureInfo.InvariantCulture) };
        var request = new PutItemRequest
        {
            TableName = Table, Item = item,
            ConditionExpression = previousVersion is null ? "attribute_not_exists(PK)" : "#v = :version"
        };
        if (previousVersion is not null)
        {
            request.ExpressionAttributeNames = new() { ["#v"] = "Version" };
            request.ExpressionAttributeValues = new() { [":version"] = new() { N = previousVersion.Value.ToString(CultureInfo.InvariantCulture) } };
        }
        try { await db.PutItemAsync(request); }
        catch (ConditionalCheckFailedException e)
        { throw new StorageConflictException("O registro já existe ou foi alterado durante a operação.", e); }
    }

    public async Task<int> NextId()
    {
        var r = await db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = Table, Key = Key("META", "TICKET_COUNTER"), UpdateExpression = "ADD NextId :one",
            ExpressionAttributeValues = new() { [":one"] = new() { N = "1" } }, ReturnValues = ReturnValue.UPDATED_NEW
        });
        return int.Parse(r.Attributes["NextId"].N, CultureInfo.InvariantCulture);
    }
}
