//The MIT License(MIT)

//Copyright(c) 2015 Alberto Rodriguez

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System.Windows.Shapes;

namespace LiveCharts.CoreComponents
{
    public class HoverableShape
    {
        /// <summary>
        /// Point of this area
        /// </summary>
        public ChartPoint Value { get; set; }
        /// <summary>
        /// Shape that fires hover
        /// </summary>
        public Shape Shape { get; set; }
        /// <summary>
        /// Shape that that changes style on hover
        /// </summary>
        public Shape Target { get; set; }
        /// <summary>
        /// serie that contains thos point
        /// </summary>
        public Series Series { get; set; }
        /// <summary>
        /// Point label
        /// </summary>
        public string Label { get; set; }
    }
}