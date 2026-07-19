using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PolymarketLab.SharedKernel.Errors;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Framework.Response;

public static class ResponseExtensions
{
    /// <summary>
    /// </summary>
    /// <typeparam name="T">Какой-то объект</typeparam>
    /// <param name="result">Хранится либо успешный результат, либо ошибка</param>
    /// <returns>Возращаем либо объект, либо список ошибок</returns>
    public static ActionResult<T> ToResponseErrorOrResult<T>(this Result<T, ErrorList> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(Envelope.Ok(result.Value));

        List<ResponseError> responseErrors = [];
        foreach (var error in result.Error)
        {
            var invalidFiled = string.IsNullOrWhiteSpace(error.InvalidField) ? null : error.InvalidField;
            var responseError = new ResponseError(error.Code, error.Message, invalidFiled);
            responseErrors.Add(responseError);
        }

        var statusCode = result.Error.ToList()[0].Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.ValueIsInvalid => StatusCodes.Status400BadRequest,
            ErrorType.InvalidLength => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        var envelope = Envelope.Errors(responseErrors);

        return new ObjectResult(envelope)
        {
            StatusCode = statusCode
        };
    }

    /// <summary>
    ///     Response -расширение когда нужно вернуть либо Ок либо Error
    /// </summary>
    /// <param name="result">Резульат</param>
    /// <returns>Ок либо Error</returns>
    public static ActionResult ToResponseOkOrError(this UnitResult<ErrorList> result)
    {
        if (result.IsSuccess)
            return new OkResult();

        List<ResponseError> responseErrors = [];
        foreach (var error in result.Error)
        {
            var invalidFiled = string.IsNullOrWhiteSpace(error.InvalidField) ? null : error.InvalidField;
            var responseError = new ResponseError(error.Code, error.Message, invalidFiled);
            responseErrors.Add(responseError);
        }

        var statusCode = result.Error.ToList()[0].Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.ValueIsInvalid => StatusCodes.Status400BadRequest,
            ErrorType.InvalidLength => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        var envelope = Envelope.Errors(responseErrors);

        return new ObjectResult(envelope)
        {
            StatusCode = statusCode
        };
    }
}
