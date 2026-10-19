namespace AltevaCrawler.Shared.Utilities;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

/// <summary>A user-presentable fetch problem: a short machine-friendly kind plus a readable message.</summary>
public readonly record struct CrawlError(string Kind, string Message);

/// <summary>
/// Turns fetch exceptions and bad responses into a <see cref="CrawlError"/> that is stored on the page and shown in the UI
/// (instead of only being written to the worker log).
/// </summary>
public static class CrawlErrorClassifier
{
    public const string Ssl = "ssl";
    public const string Timeout = "timeout";
    public const string Dns = "dns";
    public const string Connection = "connection";
    public const string Http = "http";
    public const string NonHtml = "non-html";
    public const string Storage = "storage";
    public const string Unknown = "error";

    private const int MaxMessageLength = 500;

    /// <summary>True for certificate / TLS handshake failures, which are never worth retrying.</summary>
    public static bool IsTlsError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is AuthenticationException) return true;
            if (e is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }) return true;
        }
        return false;
    }

    public static CrawlError FromException(Exception ex)
    {
        if (IsTlsError(ex))
        {
            return new CrawlError(Ssl, "SSL/TLS error: " + Trim(Innermost(ex).Message));
        }

        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }:
                    return new CrawlError(Dns, "DNS lookup failed: " + Trim(Innermost(ex).Message));
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData }:
                    return new CrawlError(Dns, "DNS lookup failed: " + Trim(e.Message));
                case SocketException se:
                    return new CrawlError(Connection, $"Connection failed ({se.SocketErrorCode}): " + Trim(e.Message));
                case HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError }:
                    return new CrawlError(Connection, "Connection failed: " + Trim(Innermost(ex).Message));
                case TimeoutException:
                case OperationCanceledException:
                    return new CrawlError(Timeout, "Request timed out.");
            }
        }

        return new CrawlError(Unknown, Trim($"{ex.GetType().Name}: {Innermost(ex).Message}"));
    }

    public static CrawlError FromStatus(int statusCode, string? reasonPhrase) =>
        new(Http, string.IsNullOrWhiteSpace(reasonPhrase) ? $"HTTP {statusCode}" : $"HTTP {statusCode} {reasonPhrase}");

    public static CrawlError FromNonHtml(string? contentType) =>
        new(NonHtml, $"Skipped: content type '{(string.IsNullOrWhiteSpace(contentType) ? "unknown" : contentType)}' is not HTML.");

    public static CrawlError FromStorageFailure() =>
        new(Storage, "The page was downloaded but could not be saved to the file store.");

    private static Exception Innermost(Exception ex)
    {
        Exception e = ex;
        while (e.InnerException != null) e = e.InnerException;
        return e;
    }

    private static string Trim(string message) =>
        message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
}
