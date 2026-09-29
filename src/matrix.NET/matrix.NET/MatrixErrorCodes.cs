namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Error codes defined by the Matrix Client-Server API, for comparing with
/// <see cref="MatrixException.ErrorCode"/>.
/// </summary>
/// <example>
/// <code>
/// catch (MatrixException e) when (e.ErrorCode == MatrixErrorCodes.Forbidden) { ... }
/// </code>
/// </example>
/// <remarks>
/// Homeservers may also return custom codes in their own namespace, e.g.
/// <c>COM.EXAMPLE_FORBIDDEN</c>. When the code is <see cref="Unknown"/>, the HTTP status code is
/// the more reliable indication of what went wrong.
/// </remarks>
public static class MatrixErrorCodes
{
    // Common error codes, which any endpoint may return

    /// <summary>The request contained valid JSON that was malformed, e.g. missing required keys.</summary>
    public const string BadJson = "M_BAD_JSON";

    /// <summary>Forbidden access, e.g. joining a room without permission or a failed login.</summary>
    public const string Forbidden = "M_FORBIDDEN";

    /// <summary>Too many requests in a short period; wait and try again.</summary>
    public const string LimitExceeded = "M_LIMIT_EXCEEDED";

    /// <summary>No access token was specified for the request.</summary>
    public const string MissingToken = "M_MISSING_TOKEN";

    /// <summary>No resource was found for this request.</summary>
    public const string NotFound = "M_NOT_FOUND";

    /// <summary>The request did not contain valid JSON.</summary>
    public const string NotJson = "M_NOT_JSON";

    /// <summary>The homeserver has reached a resource limit imposed on it.</summary>
    public const string ResourceLimitExceeded = "M_RESOURCE_LIMIT_EXCEEDED";

    /// <summary>An unknown error occurred; prefer the HTTP status code for details.</summary>
    public const string Unknown = "M_UNKNOWN";

    /// <summary>The device ID supplied by an application service does not belong to the user.</summary>
    public const string UnknownDevice = "M_UNKNOWN_DEVICE";

    /// <summary>The access or refresh token was not recognised.</summary>
    public const string UnknownToken = "M_UNKNOWN_TOKEN";

    /// <summary>The server did not understand the request, e.g. an unimplemented endpoint.</summary>
    public const string Unrecognized = "M_UNRECOGNIZED";

    /// <summary>The user has exceeded a limit associated with their account, e.g. a storage quota.</summary>
    public const string UserLimitExceeded = "M_USER_LIMIT_EXCEEDED";

    /// <summary>The account has been locked and cannot be used at this time.</summary>
    public const string UserLocked = "M_USER_LOCKED";

    /// <summary>The account has been suspended and can only be used for limited actions.</summary>
    public const string UserSuspended = "M_USER_SUSPENDED";

    // Error codes specific to certain endpoints

    /// <summary>An application service used a login type that is not supported.</summary>
    public const string AppserviceLoginUnsupported = "M_APPSERVICE_LOGIN_UNSUPPORTED";

    /// <summary>One or more aliases in an <c>m.room.canonical_alias</c> event do not point to the room.</summary>
    public const string BadAlias = "M_BAD_ALIAS";

    /// <summary>The state change cannot be performed, e.g. unbanning a user who is not banned.</summary>
    public const string BadState = "M_BAD_STATE";

    /// <summary>An application service returned a bad status.</summary>
    public const string BadStatus = "M_BAD_STATUS";

    /// <summary>The user cannot reject an invite to the server notices room.</summary>
    public const string CannotLeaveServerNoticeRoom = "M_CANNOT_LEAVE_SERVER_NOTICE_ROOM";

    /// <summary>The media ID already has content.</summary>
    public const string CannotOverwriteMedia = "M_CANNOT_OVERWRITE_MEDIA";

    /// <summary>The Captcha provided did not match what was expected.</summary>
    public const string CaptchaInvalid = "M_CAPTCHA_INVALID";

    /// <summary>A Captcha is required to complete the request.</summary>
    public const string CaptchaNeeded = "M_CAPTCHA_NEEDED";

    /// <summary>The connection to an application service failed.</summary>
    public const string ConnectionFailed = "M_CONNECTION_FAILED";

    /// <summary>The connection to an application service timed out.</summary>
    public const string ConnectionTimeout = "M_CONNECTION_TIMEOUT";

    /// <summary>The request is an attempt to send a duplicate annotation, e.g. the same reaction twice.</summary>
    public const string DuplicateAnnotation = "M_DUPLICATE_ANNOTATION";

    /// <summary>The resource is reserved by an application service, or was not created by it.</summary>
    public const string Exclusive = "M_EXCLUSIVE";

    /// <summary>The room or resource does not permit guest access.</summary>
    public const string GuestAccessForbidden = "M_GUEST_ACCESS_FORBIDDEN";

    /// <summary>The room's version is not supported by the server; see the error's <c>room_version</c>.</summary>
    public const string IncompatibleRoomVersion = "M_INCOMPATIBLE_ROOM_VERSION";

    /// <summary>A parameter has the wrong value, e.g. a string where an integer was expected.</summary>
    public const string InvalidParam = "M_INVALID_PARAM";

    /// <summary>The initial state given to <c>createRoom</c> is invalid.</summary>
    public const string InvalidRoomState = "M_INVALID_ROOM_STATE";

    /// <summary>A signature is invalid, e.g. a cross-signing key with an incorrect signature.</summary>
    public const string InvalidSignature = "M_INVALID_SIGNATURE";

    /// <summary>The user ID being registered is not valid.</summary>
    public const string InvalidUsername = "M_INVALID_USERNAME";

    /// <summary>The homeserver rejected an invite, e.g. because the invitee blocks invites.</summary>
    public const string InviteBlocked = "M_INVITE_BLOCKED";

    /// <summary>A profile key exceeds the maximum allowed length.</summary>
    public const string KeyTooLarge = "M_KEY_TOO_LARGE";

    /// <summary>A required parameter was missing from the request.</summary>
    public const string MissingParam = "M_MISSING_PARAM";

    /// <summary>The media content is not yet available.</summary>
    public const string NotYetUploaded = "M_NOT_YET_UPLOADED";

    /// <summary>Storing the value would make the profile exceed its maximum allowed size.</summary>
    public const string ProfileTooLarge = "M_PROFILE_TOO_LARGE";

    /// <summary>The room alias given to <c>createRoom</c> is already in use.</summary>
    public const string RoomInUse = "M_ROOM_IN_USE";

    /// <summary>The request used a third-party server, e.g. an identity server, that is not trusted.</summary>
    public const string ServerNotTrusted = "M_SERVER_NOT_TRUSTED";

    /// <summary>Authentication could not be performed on the third-party identifier.</summary>
    public const string ThreepidAuthFailed = "M_THREEPID_AUTH_FAILED";

    /// <summary>The server does not permit this third-party identifier.</summary>
    public const string ThreepidDenied = "M_THREEPID_DENIED";

    /// <summary>The third-party identifier is already in use.</summary>
    public const string ThreepidInUse = "M_THREEPID_IN_USE";

    /// <summary>The server does not support third-party identifiers of the given medium.</summary>
    public const string ThreepidMediumNotSupported = "M_THREEPID_MEDIUM_NOT_SUPPORTED";

    /// <summary>No record matching the third-party identifier was found.</summary>
    public const string ThreepidNotFound = "M_THREEPID_NOT_FOUND";

    /// <summary>The request or entity was too large.</summary>
    public const string TooLarge = "M_TOO_LARGE";

    /// <summary>The request was not correctly authorised, usually due to a login failure.</summary>
    public const string Unauthorized = "M_UNAUTHORIZED";

    /// <summary>The room version requested for a new room is not supported.</summary>
    public const string UnsupportedRoomVersion = "M_UNSUPPORTED_ROOM_VERSION";

    /// <summary>An application service has no URL configured.</summary>
    public const string UrlNotSet = "M_URL_NOT_SET";

    /// <summary>The user ID has been deactivated.</summary>
    public const string UserDeactivated = "M_USER_DEACTIVATED";

    /// <summary>The user ID being registered has already been taken.</summary>
    public const string UserInUse = "M_USER_IN_USE";

    /// <summary>The password is too weak.</summary>
    public const string WeakPassword = "M_WEAK_PASSWORD";

    /// <summary>The key backup version does not match the current one.</summary>
    public const string WrongRoomKeysVersion = "M_WRONG_ROOM_KEYS_VERSION";
}