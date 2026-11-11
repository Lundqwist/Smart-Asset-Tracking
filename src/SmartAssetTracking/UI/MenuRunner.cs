// ============================================================================
// MenuRunner.cs
// ----------------------------------------------------------------------------
// The reusable engine behind EVERY menu in the program (Level 2: "add
// menu-driven navigation"). A screen only describes its menu as a list of
// MenuItems (a label plus the code to run). MenuRunner then does the rest:
//
//   1. clears the screen and draws the title bar
//   2. hides items the user's role isn't allowed to see
//   3. lets the user pick an item with the arrow keys
//   4. runs the chosen item and catches any error, showing a friendly
//      message instead of crashing (the assignment's "exception handling")
//   5. repeats until the user picks "Back"
// ============================================================================

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>A single menu entry. Hidden for users below <see cref="MinRole"/>.</summary>
// Func<Task> is a "delegate": a variable that holds a METHOD. Here it is an async
// method with no parameters, and the menu calls it when the item is chosen.
// PauseAfter: should we wait for a key press after the action finishes?
public record MenuItem(string Label, Func<Task> Action, UserRole MinRole = UserRole.Employee, bool PauseAfter = true);

/// <summary>Runs arrow-key driven menus and turns exceptions into friendly messages.</summary>
public class MenuRunner(UserSession session, ILogger<MenuRunner> logger)
{
    /// <summary>Shows the menu until the user chooses the back item (or the session ends).</summary>
    // drawExtra is optional code that draws something under the title (e.g. a welcome message).
    public async Task RunAsync(string title, IReadOnlyList<MenuItem> items, string backLabel = "↩ Back",
        Action? drawExtra = null)
    {
        // Keep showing the menu while someone is logged in. "Exit program" logs out,
        // which also ends this loop.
        while (session.IsAuthenticated)
        {
            ConsoleUi.Header(title, session);
            drawExtra?.Invoke(); // run drawExtra only if it isn't null

            // Role-based menu: only keep items the current user is allowed to use.
            var visible = items.Where(i => session.HasAtLeast(i.MinRole)).ToList();

            // Add a "Back" item at the end, and number every item: (item, index).
            var back = new MenuItem(backLabel, () => Task.CompletedTask);
            var numbered = visible.Append(back).Select((item, index) => (item, index)).ToList();

            // "var (selected, _)" deconstructs the chosen tuple. "_" means "ignore this part" (the index).
            // {x.index + 1,2} prints the number right-aligned in 2 characters (" 1", "10").
            var (selected, _) = ConsoleInput.Select("What would you like to do?", numbered,
                x => x.item == back ? $"   {x.item.Label}" : $"{x.index + 1,2}. {x.item.Label}", 15);

            if (selected == back)
                return; // leave this menu and go back to the previous one

            ConsoleUi.Header(selected.Label, session);
            var ok = await ExecuteAsync(selected.Action);

            // After an error we always pause, so the user can read the message.
            if (selected.PauseAfter || !ok)
                ConsoleUi.Pause();
        }
    }

    /// <summary>Runs an action and shows a readable message for any failure. Returns false on error.</summary>
    // try/catch: code inside "try" runs normally. If it throws an exception, C#
    // jumps to the first "catch" whose type matches. The order matters: specific
    // types come first, and the general "Exception" catches everything else at the end.
    public async Task<bool> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ValidationException ex)
        {
            // The user typed something invalid. Show all problems in a red box.
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
            // The database refused to save, e.g. a duplicate value. The real reason is in InnerException.
            logger.LogError(ex, "Database update failed");
            ConsoleUi.Error("The database rejected the change: " + (ex.InnerException?.Message ?? ex.Message));
        }
        catch (SqlException ex)
        {
            // SQL Server problem (e.g. the server stopped).
            logger.LogError(ex, "Database error");
            ConsoleUi.Error("A database error occurred: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // No internet, or the exchange-rate website is down.
            logger.LogWarning(ex, "HTTP request failed");
            ConsoleUi.Error("Could not reach the online service: " + ex.Message);
        }
        catch (IOException ex)
        {
            // Problem writing an export file (e.g. the disk is full or the file is open in Excel).
            logger.LogError(ex, "File error");
            ConsoleUi.Error("File error: " + ex.Message);
        }
        catch (Exception ex)
        {
            // Anything we didn't expect: log the full details, show a short message, keep running.
            logger.LogError(ex, "Unexpected error");
            ConsoleUi.Error("Unexpected error: " + ex.Message);
            AnsiConsole.MarkupLine("[grey]Details have been written to the log file.[/]");
        }

        return false;
    }
}
