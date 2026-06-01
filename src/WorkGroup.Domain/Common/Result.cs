namespace WorkGroup.Domain.Common;

/// <summary>
/// 도메인/애플리케이션 경계에서 성공·실패를 예외 없이 표현하는 결과 타입.
/// 실패 사유는 사용자에게 보일 수 있는 한글 메시지로 담는다(plan.md D14).
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsFailure => !IsSuccess;

    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
}

/// <summary>
/// 값을 동반하는 결과 타입. 성공 시 <see cref="Value"/>가 채워진다.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? error) : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>성공 결과의 값. 실패 결과에서 접근하면 예외를 던진다.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("실패한 결과에서는 값을 읽을 수 없습니다.");

    public static Result<T> Ok(T value) => new(true, value, null);
    public static new Result<T> Fail(string error) => new(false, default, error);
}
