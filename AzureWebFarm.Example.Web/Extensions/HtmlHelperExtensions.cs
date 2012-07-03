using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Web.Mvc;
using System.Web.Mvc.Html;

namespace AzureWebFarm.Example.Web.Extensions
{
    public static class HtmlHelperExtensions
    {
        public static MvcHtmlString DisplayHelpFor<TModel, TValue>(this HtmlHelper<TModel> html, Expression<Func<TModel, TValue>> expression, string templateName)
        {
            Expression expressionBody = expression.Body;

            if (expressionBody is MemberExpression)
            {
                var memberExpression = (MemberExpression)expressionBody;
                string propertyName = memberExpression.Member.Name;
                
                return html.DisplayFor(expression, templateName, new { Message = html.ViewData.ModelMetadata.Properties.Single(p => p.PropertyName == propertyName).Description });
            }
            else
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "The supplied expression <{0}> isn't a MemberExpression.", expression.ToString()));
            }
        }
    }
}