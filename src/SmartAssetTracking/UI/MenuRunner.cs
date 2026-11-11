using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>A single menu entry. Hidden for users below <see cref="MinRole"/>.</summary>
public record MenuItem(string Label, Func<Task> Action, UserRole MinRole = UserRole.Employee, bool PauseAfter = true);

/// <summary>Runs arrow-key driven menus and turns exceptions into friendly messages.</summary>
public class MenuRunner(UserSession session, ILogger<MenuRunner> logger)
{
    /// <summary>Shows the menu until the user chooses the back item (or the session ends).</summary>
    public async Task RunAsync(string title, IReadOnlyList<MenuItem> items, string backLabel = "↩ Back",
        Action? drawExtra = null)
    {
        while (session.IsAuthenticated)
        {
            ConsoleUi.Header(title, session);
            drawExtra?.Invoke();

            var visible = items.Where(i => session.HasAtLeast(i.MinRole)).ToList();
            var back = new MenuItem(backLabel, () => Task.CompletedTask);
            var numbered = visible.Append(back).Select((item, index) => (item, index)).ToList();

            var (selected, _) = ConsoleInput.Select("What would you like to do?", numbered,
                x => x.item == back ? $"   {x.item.Label}" : $"{x.index + 1,2}. {x.item.Label}", 15);

            if (selected == back)
                return;

            ConsoleUi.Header(selected.Label, session);
            var ok = await ExecuteAsync(selected.Action);
            if (selected.PauseAfter || !ok)
                ConsoleUi.Pause();
        }
    }

    /// <summary>Runs an action and shows a readable message for any failure. Returns false on error.</summary>
    public async Task<bool> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ValidationException ex)
        {
            ConsoleUi.Errors(ex.Errors);
        }
        catch (NotFoundException ex)
        {
            ConsoleUi.Error(ex.Message);
        }
        catch (AccessDeniedException ex)
        {
            ConsoleUi.Error(ex.Message);
            logger.LogWarning("Access denied for {User}: {Message}", session.CurrentUser?.Username, ex.Message);
        }
        catch (OperationCanceledException)
        {
            ConsoleUi.Warning("Operation cancelled.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Database update failed");
            ConsoleUi.Error("The database rejected the change: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Database error");
            ConsoleUi.Error("A database error occurred: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTTP request failed");
            ConsoleUi.Error("Could not reach the online service: " + ex.Message);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "File error");
            ConsoleUi.Error("File error: " + ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error");
            ConsoleUi.Error("Unexpected error: " + ex.Message);
            AnsiConsole.MarkupLine("[grey]Details have been written to the log file.[/]");
        }

        return false;
    }
}
