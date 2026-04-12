// Shape Manager - renders basic shapes (SVG) on the dashboard canvas
class ShapeManager {
    static SHAPE_DEFS = [
        { id: 'shape-line', name: 'Line', icon: 'bi-dash-lg', description: 'A straight line', chartJsType: 'shape' },
        { id: 'shape-rect', name: 'Rectangle', icon: 'bi-square', description: 'A rectangle', chartJsType: 'shape' },
        { id: 'shape-roundedRect', name: 'Rounded Rect', icon: 'bi-app', description: 'A rounded rectangle', chartJsType: 'shape' },
        { id: 'shape-circle', name: 'Circle', icon: 'bi-circle', description: 'A circle', chartJsType: 'shape' },
        { id: 'shape-ellipse', name: 'Ellipse', icon: 'bi-record-circle', description: 'An ellipse', chartJsType: 'shape' },
        { id: 'shape-triangle', name: 'Triangle', icon: 'bi-triangle', description: 'A triangle', chartJsType: 'shape' },
        { id: 'shape-diamond', name: 'Diamond', icon: 'bi-diamond', description: 'A diamond shape', chartJsType: 'shape' },
        { id: 'shape-textbox', name: 'Text Box', icon: 'bi-fonts', description: 'An editable text box', chartJsType: 'shape' },
        { id: 'shape-arrow-right', name: 'Arrow Right', icon: 'bi-arrow-right', description: 'Right arrow', chartJsType: 'shape' },
        { id: 'shape-arrow-left', name: 'Arrow Left', icon: 'bi-arrow-left', description: 'Left arrow', chartJsType: 'shape' },
    ];

    static getLibraryGroup() {
        return { group: 'Shapes', charts: ShapeManager.SHAPE_DEFS };
    }

    static isShape(chartType) {
        return chartType && chartType.startsWith('shape-');
    }

    static getDefaultShapeProps(shapeType) {
        return {
            fillColor: shapeType === 'shape-textbox' ? 'transparent' : '#5B9BD5',
            strokeColor: '#3A7BBF',
            strokeWidth: 2,
            opacity: 1,
            text: shapeType === 'shape-textbox' ? 'Double-click to edit' : '',
            fontSize: 16,
            fontColor: '#1E2D3D',
            textAlign: 'center',
            fontWeight: 'normal',
            cornerRadius: shapeType === 'shape-roundedRect' ? 12 : 0,
        };
    }

    static render(wrapEl, chartDef) {
        const props = chartDef.shapeProps || ShapeManager.getDefaultShapeProps(chartDef.chartType);
        wrapEl.innerHTML = '';
        wrapEl.classList.add('shape-canvas-wrap');

        const svgNS = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(svgNS, 'svg');
        svg.setAttribute('width', '100%');
        svg.setAttribute('height', '100%');
        svg.setAttribute('viewBox', '0 0 200 120');
        svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
        svg.style.display = 'block';

        const fill = props.fillColor || 'transparent';
        const stroke = props.strokeColor || '#3A7BBF';
        const sw = props.strokeWidth || 2;
        const opacity = props.opacity ?? 1;

        let el;
        switch (chartDef.chartType) {
            case 'shape-line':
                el = document.createElementNS(svgNS, 'line');
                el.setAttribute('x1', '10'); el.setAttribute('y1', '60');
                el.setAttribute('x2', '190'); el.setAttribute('y2', '60');
                el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw);
                el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-rect':
                el = document.createElementNS(svgNS, 'rect');
                el.setAttribute('x', '5'); el.setAttribute('y', '5');
                el.setAttribute('width', '190'); el.setAttribute('height', '110');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-roundedRect':
                el = document.createElementNS(svgNS, 'rect');
                el.setAttribute('x', '5'); el.setAttribute('y', '5');
                el.setAttribute('width', '190'); el.setAttribute('height', '110');
                el.setAttribute('rx', props.cornerRadius || 12);
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-circle':
                el = document.createElementNS(svgNS, 'circle');
                el.setAttribute('cx', '100'); el.setAttribute('cy', '60'); el.setAttribute('r', '50');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-ellipse':
                el = document.createElementNS(svgNS, 'ellipse');
                el.setAttribute('cx', '100'); el.setAttribute('cy', '60');
                el.setAttribute('rx', '90'); el.setAttribute('ry', '50');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-triangle':
                el = document.createElementNS(svgNS, 'polygon');
                el.setAttribute('points', '100,8 190,112 10,112');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-diamond':
                el = document.createElementNS(svgNS, 'polygon');
                el.setAttribute('points', '100,5 195,60 100,115 5,60');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-arrow-right':
                el = document.createElementNS(svgNS, 'polygon');
                el.setAttribute('points', '10,35 140,35 140,15 190,60 140,105 140,85 10,85');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-arrow-left':
                el = document.createElementNS(svgNS, 'polygon');
                el.setAttribute('points', '190,35 60,35 60,15 10,60 60,105 60,85 190,85');
                el.setAttribute('fill', fill); el.setAttribute('stroke', stroke);
                el.setAttribute('stroke-width', sw); el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;

            case 'shape-textbox':
            default:
                el = document.createElementNS(svgNS, 'rect');
                el.setAttribute('x', '2'); el.setAttribute('y', '2');
                el.setAttribute('width', '196'); el.setAttribute('height', '116');
                el.setAttribute('fill', fill === 'transparent' ? 'transparent' : fill);
                el.setAttribute('stroke', stroke); el.setAttribute('stroke-width', sw);
                el.setAttribute('stroke-dasharray', '4,3');
                el.setAttribute('opacity', opacity);
                svg.appendChild(el);
                break;
        }

        wrapEl.appendChild(svg);

        if (chartDef.chartType === 'shape-textbox') {
            const textDiv = document.createElement('div');
            textDiv.className = 'shape-text-overlay';
            textDiv.style.fontSize = (props.fontSize || 16) + 'px';
            textDiv.style.color = props.fontColor || '#1E2D3D';
            textDiv.style.textAlign = props.textAlign || 'center';
            textDiv.style.fontWeight = props.fontWeight || 'normal';
            textDiv.textContent = props.text || '';
            textDiv.contentEditable = 'true';
            textDiv.spellcheck = false;
            textDiv.addEventListener('blur', () => {
                if (!chartDef.shapeProps) chartDef.shapeProps = {};
                chartDef.shapeProps.text = textDiv.textContent;
                if (window.canvasManager) {
                    const chart = window.canvasManager.charts.find(c => c.id === chartDef.id);
                    if (chart) {
                        if (!chart.shapeProps) chart.shapeProps = {};
                        chart.shapeProps.text = textDiv.textContent;
                        fetch(`/api/chart/${chartDef.id}`, {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(chart)
                        }).catch(() => {});
                    }
                }
            });
            wrapEl.appendChild(textDiv);
        }
    }
}

window.ShapeManager = ShapeManager;
