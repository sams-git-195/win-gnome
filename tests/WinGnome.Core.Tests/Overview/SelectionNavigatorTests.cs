using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;

namespace WinGnome.Core.Tests.Overview;

public class SelectionNavigatorTests
{
    // Two rows: [0][1][2] above [3][4] (second row centred under the first).
    private static readonly LayoutRect[] TwoRows =
    [
        new(0, 0, 100, 80),
        new(120, 0, 100, 80),
        new(240, 0, 100, 80),
        new(60, 100, 100, 80),
        new(180, 100, 100, 80),
    ];

    [Fact]
    public void FindNeighbor_NoItems_ReturnsMinusOne()
    {
        Assert.Equal(-1, SelectionNavigator.FindNeighbor([], 0, NavigationDirection.Right));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void FindNeighbor_NothingSelected_SelectsFirst(int current)
    {
        Assert.Equal(0, SelectionNavigator.FindNeighbor(TwoRows, current, NavigationDirection.Down));
    }

    [Theory]
    [InlineData(0, NavigationDirection.Right, 1)]
    [InlineData(1, NavigationDirection.Right, 2)]
    [InlineData(2, NavigationDirection.Left, 1)]
    [InlineData(3, NavigationDirection.Right, 4)]
    [InlineData(4, NavigationDirection.Left, 3)]
    public void FindNeighbor_MovesAlongARow(int current, NavigationDirection direction, int expected)
    {
        Assert.Equal(expected, SelectionNavigator.FindNeighbor(TwoRows, current, direction));
    }

    [Theory]
    [InlineData(0, NavigationDirection.Down, 3)]
    [InlineData(2, NavigationDirection.Down, 4)]
    [InlineData(3, NavigationDirection.Up, 0)]
    [InlineData(4, NavigationDirection.Up, 1)]
    public void FindNeighbor_MovesBetweenRows_PreferringTheNearestColumn(int current, NavigationDirection direction, int expected)
    {
        Assert.Equal(expected, SelectionNavigator.FindNeighbor(TwoRows, current, direction));
    }

    [Theory]
    [InlineData(0, NavigationDirection.Left)]
    [InlineData(0, NavigationDirection.Up)]
    [InlineData(2, NavigationDirection.Right)]
    [InlineData(4, NavigationDirection.Down)]
    public void FindNeighbor_AtAnEdge_StaysPut(int current, NavigationDirection direction)
    {
        Assert.Equal(current, SelectionNavigator.FindNeighbor(TwoRows, current, direction));
    }

    [Fact]
    public void FindNeighbor_PrefersInLineOverDiagonal()
    {
        LayoutRect[] items =
        [
            new(0, 0, 10, 10),
            new(100, 0, 10, 10),   // straight right, far
            new(40, 40, 10, 10),   // diagonal, nearer along the axis
        ];

        Assert.Equal(1, SelectionNavigator.FindNeighbor(items, 0, NavigationDirection.Right));
    }

    [Fact]
    public void MoveInGrid_NoItems_ReturnsMinusOne()
    {
        Assert.Equal(-1, SelectionNavigator.MoveInGrid(0, 0, 4, NavigationDirection.Right));
    }

    [Fact]
    public void MoveInGrid_NothingSelected_SelectsFirst()
    {
        Assert.Equal(0, SelectionNavigator.MoveInGrid(-1, 10, 4, NavigationDirection.Down));
    }

    [Theory]
    [InlineData(0, NavigationDirection.Left, 0)]
    [InlineData(0, NavigationDirection.Right, 1)]
    [InlineData(3, NavigationDirection.Right, 4)]   // wraps to the next row
    [InlineData(4, NavigationDirection.Left, 3)]    // wraps to the previous row
    [InlineData(9, NavigationDirection.Right, 9)]   // last item
    [InlineData(1, NavigationDirection.Down, 5)]
    [InlineData(5, NavigationDirection.Up, 1)]
    [InlineData(2, NavigationDirection.Up, 2)]      // top row
    [InlineData(5, NavigationDirection.Down, 9)]    // next row is short: land on the last item
    [InlineData(7, NavigationDirection.Down, 9)]
    [InlineData(9, NavigationDirection.Down, 9)]    // already on the last row
    public void MoveInGrid_MovesWithinA4ColumnGridOf10(int current, NavigationDirection direction, int expected)
    {
        Assert.Equal(expected, SelectionNavigator.MoveInGrid(current, 10, 4, direction));
    }

    [Fact]
    public void MoveInGrid_ZeroColumns_IsTreatedAsOne()
    {
        Assert.Equal(1, SelectionNavigator.MoveInGrid(0, 3, 0, NavigationDirection.Down));
    }
}
