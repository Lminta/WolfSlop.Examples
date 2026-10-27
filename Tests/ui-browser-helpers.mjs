// Inspect the actual engine display list; these helpers are installed only by browser checks.
export const uiInspectionSource = `
    Object.defineProperties(window, {
        __uiFrame: {get: () => document.querySelector('.wolf-ui-canvas')?.__uiFrame},
        __uiImages: {get: () => (window.__uiFrame?.nodes ?? []).filter(node => node.kind === 'image' && node.id.startsWith('view.'))},
        __uiImageCount: {get: () => document.querySelector('.wolf-ui-canvas')?.uiStats.imageCount ?? 0},
        __uiDraws: {get: () => document.querySelector('.wolf-ui-canvas')?.uiStats.draws ?? 0},
        __uiWheelAngle: {get: () => {const node=window.__uiImages.find(node=>node.id==='view.wheel');return node?Math.atan2(node.transform.b,node.transform.a):0;}},
        __uiHandAnchor: {get: () => {const node=window.__uiImages.find(node=>node.id==='view.right-hand');return node?(node.transform.b*node.width*.5+node.transform.d*node.height+node.transform.f)/window.__uiFrame.height:0;}},
        __uiMap: {get: () => window.__uiFrame?.nodes.find(node => node.id === 'race-map')},
        __uiOpponents: {get: () => (window.__uiMap?.drawing?.shapes ?? []).filter(shape => shape.kind === 'circle' && ['#275cff','#32ed39'].includes(shape.fill)).map(shape=>shape.points.slice(0,2))}
    });
`;
