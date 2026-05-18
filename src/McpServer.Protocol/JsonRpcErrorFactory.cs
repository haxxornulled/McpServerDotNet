using System.Text.Json;
using McpServer.Protocol.JsonRpc;

namespace McpServer.Protocol;

public static class JsonRpcErrorFactory
{
    public static JsonRpcResponse MethodNotFound(JsonElement? id, string method) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32601, Message: $"Method not found: {method}"));

    public static JsonRpcResponse InvalidParams(JsonElement? id, string message) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32602, Message: message));

    public static JsonRpcResponse InvalidRequest(JsonElement? id, string message) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32600, Message: message));

    public static JsonRpcResponse InternalError(JsonElement? id, string message) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32603, Message: message));

    public static JsonRpcResponse ServerError(JsonElement? id, string message) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32000, Message: message));

    public static JsonRpcResponse SessionNotReady(JsonElement? id) =>
        new(JsonRpc: "2.0", Id: id, Error: new JsonRpcError(Code: -32002, Message: "Session is not initialized"));
}
