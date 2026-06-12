namespace Application.Abstractions.Notification;

public interface IUserNotificationService
{
    void ShowInformation(string message);
    void ShowWarning(string message);
}
