using System.Windows;
using System.Windows.Controls;
namespace LummaSimulator.App;
// Equal height within each row, measured from content; expands one row without enlarging every row.
public sealed class EqualRowPanel:Panel
{
    public static readonly DependencyProperty ColumnsProperty=DependencyProperty.Register(nameof(Columns),typeof(int),typeof(EqualRowPanel),new FrameworkPropertyMetadata(3,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int Columns{get=>(int)GetValue(ColumnsProperty);set=>SetValue(ColumnsProperty,value);}
    private double[] heights=[];
    protected override Size MeasureOverride(Size availableSize){
        var columns=Math.Clamp(Columns,1,6);var width=double.IsInfinity(availableSize.Width)?columns*260d:Math.Max(0,availableSize.Width);
        heights=new double[(InternalChildren.Count+columns-1)/columns];
        for(var i=0;i<InternalChildren.Count;i++){var child=InternalChildren[i];child.Measure(new Size(width/columns,double.PositiveInfinity));heights[i/columns]=Math.Max(heights[i/columns],child.DesiredSize.Height);}
        return new Size(width,heights.Sum());
    }
    protected override Size ArrangeOverride(Size finalSize){
        var columns=Math.Clamp(Columns,1,6);double y=0;
        for(var i=0;i<InternalChildren.Count;i++){var row=i/columns;if(i>0&&i%columns==0)y+=heights[row-1];InternalChildren[i].Arrange(new Rect(i%columns*finalSize.Width/columns,y,finalSize.Width/columns,heights[row]));}
        return finalSize;
    }
}
