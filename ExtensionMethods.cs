using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribesLauncherSharp
{
    public static class ExtensionMethods
    {
        public delegate void HandleException(Exception e);

        /// <summary>
        /// Run a task to 'fire and forget', but to run a delegate on exception
        /// </summary>
        /// <param name="task">The Task to run</param>
        /// <param name="onError">Called if the task raises an exception</param>
        public static async void FireAndForget(this Task task, HandleException onException)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                onException?.Invoke(e);
            }
        }

        /// <summary>
        /// Asynchronously wait for a predicate function to return true, up to some timeout
        /// </summary>
        /// <param name="predicate">The predicate to check</param>
        /// <param name="timeoutMS">Timeout for the wait; if less than zero, no timeout is applied</param>
        /// <param name="intervalMS">Interval to poll the predicate at</param>
        /// <returns>True if the condition became true, false if ended due to timeout</returns>
        public static async Task<bool> WaitUntilTrue(this Func<bool> predicate, int timeoutMS = -1, int intervalMS = 100)
        {
            DateTime? expiry = null;
            if (timeoutMS >= 0)
            {
                expiry = DateTime.Now + TimeSpan.FromMilliseconds(timeoutMS);
            }

            while (!predicate())
            {
                if (expiry != null && DateTime.Now > expiry) return false;
                await Task.Delay(intervalMS);
            }

            return true;
        }
    }
}
