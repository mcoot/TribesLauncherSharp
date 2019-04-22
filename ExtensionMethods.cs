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
    }
}
