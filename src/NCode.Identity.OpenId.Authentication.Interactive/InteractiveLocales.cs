// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The locales the interactive surface ships, which <c>AddInteractiveAuthentication</c> registers in the order of
/// <see cref="All"/>, English first, ahead of the locales a host registers under other tags, whatever the order of
/// the registration calls, so English is the surface's default language for a tenant that sets no
/// <c>default_ui_locale</c>. A host that changes the wording of a page registers a locale built from one of these
/// with its own <see cref="InteractiveMessages"/> through <c>AddInteractiveLocale</c>, which takes the shipped
/// locale's place.
/// </summary>
/// <remarks>
/// The single-page bundle that mounts over the server-rendered pages carries the same wording for each of these
/// locales in catalogs of its own, so the page the server renders and the page the bundle draws over it read the
/// same.
/// </remarks>
// The bundle's catalogs are frontend/apps/auth/src/i18n/{en,de,es,fr}.ts. A change to a page's wording in one of
// these locales is made in both: InteractiveLocalesTests reads the catalogs from the build output and fails when a
// message of a shipped locale is missing from its catalog. See ADR-1402.
[PublicAPI]
public static class InteractiveLocales
{
    /// <summary>
    /// Gets the English locale, <c>en</c>.
    /// </summary>
    public static InteractiveLocale English { get; } =
        new(
            "en",
            new InteractiveMessages
            {
                SignInTitle = "Sign in",
                UsernameLabel = "Username",
                PasswordLabel = "Password",
                SignInButton = "Sign in",
                AuthorizeAccessTitle = "Authorize access",
                ConsentPromptFormat =
                    "{0} is requesting access to your account. Choose what to allow:",
                DenyButton = "Deny",
                AllowButton = "Allow",
                SignOutTitle = "Sign out",
                SignOutQuestion = "Do you want to sign out?",
                StaySignedInButton = "Stay signed in",
                SignOutButton = "Sign out",
                AuthorizationErrorTitle = "Authorization error",
                InvalidCredentialsError = "The username or password is incorrect.",
                MissingCredentialsError = "Enter both a username and a password.",
                AccessDeniedError = "This account cannot sign in. Contact your administrator.",
                SignInFailedError = "Sign-in failed. Please try again.",
                ForgotPasswordLink = "Forgot your password?",
                PasswordResetStatus =
                    "Your password has been reset. Sign in with your new password.",
                ForgotPasswordTitle = "Reset your password",
                ForgotPasswordPrompt =
                    "Enter your username. If its account has a verified email address, we will send a code to it.",
                SendCodeButton = "Send code",
                ResetPasswordTitle = "Choose a new password",
                ResetPasswordPrompt = "Enter the code from the email and choose a new password.",
                CodeLabel = "Code",
                NewPasswordLabel = "New password",
                ConfirmPasswordLabel = "Confirm new password",
                ResetPasswordButton = "Reset password",
                SignInCodeTitle = "Check your email",
                SignInCodePrompt =
                    "Enter the code we sent to your email address to finish signing in.",
                VerifyCodeButton = "Verify",
                ResendCodeButton = "Send a new code",
                CodeSentStatus = "We sent a new code.",
                InvalidCodeError = "The code is incorrect or has expired.",
                TooManyCodesError = "Too many codes were requested. Try again later.",
                PasswordMismatchError = "The passwords do not match.",
                PasswordRefusedError =
                    "Choose a different password: this one does not meet the requirements.",
                NoEmailError = "This account has no email address.",
                EmailCodeFailedError = "Something went wrong. Please try again.",
                MissingFieldsError = "Fill in every field.",
                VerificationMailSubject = "Verify your email address",
                VerificationMailBodyFormat =
                    "Your verification code is {0}. It expires in {1} minutes.\n\nIf you did not ask for this code, you can ignore this email.",
                PasswordResetMailSubject = "Reset your password",
                PasswordResetMailBodyFormat =
                    "Your password reset code is {0}. It expires in {1} minutes.\n\nIf you did not ask to reset your password, you can ignore this email. Your password has not changed.",
                SignInMailSubject = "Your sign-in code",
                SignInMailBodyFormat =
                    "Your sign-in code is {0}. It expires in {1} minutes.\n\nIf you are not signing in, someone else may know your password. Change it.",
            }
        );

    /// <summary>
    /// Gets the German locale, <c>de</c>.
    /// </summary>
    public static InteractiveLocale German { get; } =
        new(
            "de",
            new InteractiveMessages
            {
                SignInTitle = "Anmelden",
                UsernameLabel = "Benutzername",
                PasswordLabel = "Passwort",
                SignInButton = "Anmelden",
                AuthorizeAccessTitle = "Zugriff autorisieren",
                ConsentPromptFormat =
                    "{0} fordert Zugriff auf Ihr Konto an. Wählen Sie, was Sie erlauben möchten:",
                DenyButton = "Ablehnen",
                AllowButton = "Erlauben",
                SignOutTitle = "Abmelden",
                SignOutQuestion = "Möchten Sie sich abmelden?",
                StaySignedInButton = "Angemeldet bleiben",
                SignOutButton = "Abmelden",
                AuthorizationErrorTitle = "Autorisierungsfehler",
                InvalidCredentialsError = "Der Benutzername oder das Passwort ist falsch.",
                MissingCredentialsError =
                    "Geben Sie sowohl einen Benutzernamen als auch ein Passwort ein.",
                AccessDeniedError =
                    "Dieses Konto kann sich nicht anmelden. Wenden Sie sich an Ihren Administrator.",
                SignInFailedError =
                    "Die Anmeldung ist fehlgeschlagen. Bitte versuchen Sie es erneut.",
                ForgotPasswordLink = "Passwort vergessen?",
                PasswordResetStatus =
                    "Ihr Passwort wurde zurückgesetzt. Melden Sie sich mit Ihrem neuen Passwort an.",
                ForgotPasswordTitle = "Passwort zurücksetzen",
                ForgotPasswordPrompt =
                    "Geben Sie Ihren Benutzernamen ein. Hat das Konto eine bestätigte E-Mail-Adresse, senden wir einen Code dorthin.",
                SendCodeButton = "Code senden",
                ResetPasswordTitle = "Neues Passwort wählen",
                ResetPasswordPrompt =
                    "Geben Sie den Code aus der E-Mail ein und wählen Sie ein neues Passwort.",
                CodeLabel = "Code",
                NewPasswordLabel = "Neues Passwort",
                ConfirmPasswordLabel = "Neues Passwort bestätigen",
                ResetPasswordButton = "Passwort zurücksetzen",
                SignInCodeTitle = "Prüfen Sie Ihre E-Mail",
                SignInCodePrompt =
                    "Geben Sie den Code ein, den wir an Ihre E-Mail-Adresse gesendet haben, um die Anmeldung abzuschließen.",
                VerifyCodeButton = "Bestätigen",
                ResendCodeButton = "Neuen Code senden",
                CodeSentStatus = "Wir haben einen neuen Code gesendet.",
                InvalidCodeError = "Der Code ist falsch oder abgelaufen.",
                TooManyCodesError =
                    "Es wurden zu viele Codes angefordert. Versuchen Sie es später erneut.",
                PasswordMismatchError = "Die Passwörter stimmen nicht überein.",
                PasswordRefusedError =
                    "Wählen Sie ein anderes Passwort: Dieses erfüllt die Anforderungen nicht.",
                NoEmailError = "Dieses Konto hat keine E-Mail-Adresse.",
                EmailCodeFailedError = "Etwas ist schiefgelaufen. Bitte versuchen Sie es erneut.",
                MissingFieldsError = "Füllen Sie alle Felder aus.",
                VerificationMailSubject = "E-Mail-Adresse bestätigen",
                VerificationMailBodyFormat =
                    "Ihr Bestätigungscode lautet {0}. Er läuft in {1} Minuten ab.\n\nWenn Sie diesen Code nicht angefordert haben, können Sie diese E-Mail ignorieren.",
                PasswordResetMailSubject = "Passwort zurücksetzen",
                PasswordResetMailBodyFormat =
                    "Ihr Code zum Zurücksetzen des Passworts lautet {0}. Er läuft in {1} Minuten ab.\n\nWenn Sie das Zurücksetzen Ihres Passworts nicht angefordert haben, können Sie diese E-Mail ignorieren. Ihr Passwort wurde nicht geändert.",
                SignInMailSubject = "Ihr Anmeldecode",
                SignInMailBodyFormat =
                    "Ihr Anmeldecode lautet {0}. Er läuft in {1} Minuten ab.\n\nWenn Sie sich nicht anmelden, kennt möglicherweise jemand anderes Ihr Passwort. Ändern Sie es.",
            }
        );

    /// <summary>
    /// Gets the Spanish locale, <c>es</c>.
    /// </summary>
    public static InteractiveLocale Spanish { get; } =
        new(
            "es",
            new InteractiveMessages
            {
                SignInTitle = "Iniciar sesión",
                UsernameLabel = "Nombre de usuario",
                PasswordLabel = "Contraseña",
                SignInButton = "Iniciar sesión",
                AuthorizeAccessTitle = "Autorizar acceso",
                ConsentPromptFormat = "{0} solicita acceso a su cuenta. Elija qué permitir:",
                DenyButton = "Denegar",
                AllowButton = "Permitir",
                SignOutTitle = "Cerrar sesión",
                SignOutQuestion = "¿Desea cerrar sesión?",
                StaySignedInButton = "Seguir conectado",
                SignOutButton = "Cerrar sesión",
                AuthorizationErrorTitle = "Error de autorización",
                InvalidCredentialsError = "El nombre de usuario o la contraseña no son correctos.",
                MissingCredentialsError = "Introduzca un nombre de usuario y una contraseña.",
                AccessDeniedError =
                    "Esta cuenta no puede iniciar sesión. Póngase en contacto con su administrador.",
                SignInFailedError = "No se pudo iniciar sesión. Inténtelo de nuevo.",
                ForgotPasswordLink = "¿Ha olvidado su contraseña?",
                PasswordResetStatus =
                    "Su contraseña ha sido restablecida. Inicie sesión con su nueva contraseña.",
                ForgotPasswordTitle = "Restablecer su contraseña",
                ForgotPasswordPrompt =
                    "Introduzca su nombre de usuario. Si su cuenta tiene una dirección de correo verificada, le enviaremos un código.",
                SendCodeButton = "Enviar código",
                ResetPasswordTitle = "Elegir una nueva contraseña",
                ResetPasswordPrompt =
                    "Introduzca el código del correo y elija una nueva contraseña.",
                CodeLabel = "Código",
                NewPasswordLabel = "Nueva contraseña",
                ConfirmPasswordLabel = "Confirmar la nueva contraseña",
                ResetPasswordButton = "Restablecer contraseña",
                SignInCodeTitle = "Revise su correo",
                SignInCodePrompt =
                    "Introduzca el código que enviamos a su dirección de correo para terminar de iniciar sesión.",
                VerifyCodeButton = "Verificar",
                ResendCodeButton = "Enviar un código nuevo",
                CodeSentStatus = "Hemos enviado un código nuevo.",
                InvalidCodeError = "El código es incorrecto o ha caducado.",
                TooManyCodesError =
                    "Se han solicitado demasiados códigos. Inténtelo de nuevo más tarde.",
                PasswordMismatchError = "Las contraseñas no coinciden.",
                PasswordRefusedError = "Elija otra contraseña: esta no cumple los requisitos.",
                NoEmailError = "Esta cuenta no tiene dirección de correo.",
                EmailCodeFailedError = "Algo salió mal. Inténtelo de nuevo.",
                MissingFieldsError = "Complete todos los campos.",
                VerificationMailSubject = "Verifique su dirección de correo",
                VerificationMailBodyFormat =
                    "Su código de verificación es {0}. Caduca en {1} minutos.\n\nSi no solicitó este código, puede ignorar este correo.",
                PasswordResetMailSubject = "Restablezca su contraseña",
                PasswordResetMailBodyFormat =
                    "Su código para restablecer la contraseña es {0}. Caduca en {1} minutos.\n\nSi no solicitó restablecer su contraseña, puede ignorar este correo. Su contraseña no ha cambiado.",
                SignInMailSubject = "Su código de inicio de sesión",
                SignInMailBodyFormat =
                    "Su código de inicio de sesión es {0}. Caduca en {1} minutos.\n\nSi no está iniciando sesión, es posible que otra persona conozca su contraseña. Cámbiela.",
            }
        );

    /// <summary>
    /// Gets the French locale, <c>fr</c>.
    /// </summary>
    public static InteractiveLocale French { get; } =
        new(
            "fr",
            new InteractiveMessages
            {
                SignInTitle = "Se connecter",
                UsernameLabel = "Nom d’utilisateur",
                PasswordLabel = "Mot de passe",
                SignInButton = "Se connecter",
                AuthorizeAccessTitle = "Autoriser l’accès",
                ConsentPromptFormat =
                    "{0} demande l’accès à votre compte. Choisissez ce que vous autorisez :",
                DenyButton = "Refuser",
                AllowButton = "Autoriser",
                SignOutTitle = "Se déconnecter",
                SignOutQuestion = "Voulez-vous vous déconnecter ?",
                StaySignedInButton = "Rester connecté",
                SignOutButton = "Se déconnecter",
                AuthorizationErrorTitle = "Erreur d’autorisation",
                InvalidCredentialsError = "Le nom d’utilisateur ou le mot de passe est incorrect.",
                MissingCredentialsError = "Saisissez un nom d’utilisateur et un mot de passe.",
                AccessDeniedError =
                    "Ce compte ne peut pas se connecter. Contactez votre administrateur.",
                SignInFailedError = "La connexion a échoué. Veuillez réessayer.",
                ForgotPasswordLink = "Mot de passe oublié ?",
                PasswordResetStatus =
                    "Votre mot de passe a été réinitialisé. Connectez-vous avec votre nouveau mot de passe.",
                ForgotPasswordTitle = "Réinitialiser votre mot de passe",
                ForgotPasswordPrompt =
                    "Saisissez votre nom d’utilisateur. Si son compte a une adresse e-mail vérifiée, nous y enverrons un code.",
                SendCodeButton = "Envoyer le code",
                ResetPasswordTitle = "Choisir un nouveau mot de passe",
                ResetPasswordPrompt =
                    "Saisissez le code reçu par e-mail et choisissez un nouveau mot de passe.",
                CodeLabel = "Code",
                NewPasswordLabel = "Nouveau mot de passe",
                ConfirmPasswordLabel = "Confirmer le nouveau mot de passe",
                ResetPasswordButton = "Réinitialiser le mot de passe",
                SignInCodeTitle = "Consultez votre messagerie",
                SignInCodePrompt =
                    "Saisissez le code envoyé à votre adresse e-mail pour terminer la connexion.",
                VerifyCodeButton = "Vérifier",
                ResendCodeButton = "Envoyer un nouveau code",
                CodeSentStatus = "Nous avons envoyé un nouveau code.",
                InvalidCodeError = "Le code est incorrect ou a expiré.",
                TooManyCodesError = "Trop de codes ont été demandés. Réessayez plus tard.",
                PasswordMismatchError = "Les mots de passe ne correspondent pas.",
                PasswordRefusedError =
                    "Choisissez un autre mot de passe : celui-ci ne respecte pas les exigences.",
                NoEmailError = "Ce compte n’a pas d’adresse e-mail.",
                EmailCodeFailedError = "Une erreur s’est produite. Veuillez réessayer.",
                MissingFieldsError = "Remplissez tous les champs.",
                VerificationMailSubject = "Vérifiez votre adresse e-mail",
                VerificationMailBodyFormat =
                    "Votre code de vérification est {0}. Il expire dans {1} minutes.\n\nSi vous n’avez pas demandé ce code, vous pouvez ignorer cet e-mail.",
                PasswordResetMailSubject = "Réinitialisez votre mot de passe",
                PasswordResetMailBodyFormat =
                    "Votre code de réinitialisation du mot de passe est {0}. Il expire dans {1} minutes.\n\nSi vous n’avez pas demandé la réinitialisation de votre mot de passe, vous pouvez ignorer cet e-mail. Votre mot de passe n’a pas changé.",
                SignInMailSubject = "Votre code de connexion",
                SignInMailBodyFormat =
                    "Votre code de connexion est {0}. Il expire dans {1} minutes.\n\nSi vous ne vous connectez pas, quelqu’un d’autre connaît peut-être votre mot de passe. Changez-le.",
            }
        );

    /// <summary>
    /// Gets every locale the surface ships, in the order it registers them: English first.
    /// </summary>
    public static ImmutableArray<InteractiveLocale> All { get; } =
    [English, German, Spanish, French];
}
