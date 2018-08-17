
using System;
using System.Linq.Expressions;


namespace MessageStream
{

    /// <summary>
    /// Optmization for calling new T(); 
    /// Uses an expression tree that emits the same IL as doing new MyClass(); instead of new T which uses Activator
    /// </summary>
    public static class FastActivator<T> where T : new()
    {

        private static readonly Expression<Func<T>> NewExpr = () => new T();
        public static readonly Func<T> NewInstance = NewExpr.Compile();

    }
}
