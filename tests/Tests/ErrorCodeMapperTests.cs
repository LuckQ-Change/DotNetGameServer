using Core;

namespace Tests;

public class ErrorCodeMapperTests
{
    [Theory]
    [InlineData("OK", 0)]
    [InlineData("ERROR", 500)]
    [InlineData("INTERNAL", 500)]
    [InlineData("AUTH_REQUIRED", 401)]
    [InlineData("UNAUTHORIZED", 401)]
    [InlineData("FORBIDDEN", 403)]
    [InlineData("BAD_REQUEST", 400)]
    [InlineData("NOT_FOUND", 404)]
    [InlineData("RATE_LIMIT", 429)]
    [InlineData("TOO_MANY_REQUESTS", 429)]
    [InlineData("SERVICE_NOT_READY", 503)]
    [InlineData("UNKNOWN_TYPE", 400)]
    [InlineData("", 400)]
    [InlineData(null, 400)]
    [InlineData("404", 404)]
    [InlineData("200", 200)]
    public void Map_ReturnsExpectedCode(string? input, int expected)
    {
        Assert.Equal(expected, ErrorCodeMapper.Map(input));
    }
}
