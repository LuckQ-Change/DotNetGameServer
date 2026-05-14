(function(){let e=document.createElement(`link`).relList;if(e&&e.supports&&e.supports(`modulepreload`))return;for(let e of document.querySelectorAll(`link[rel="modulepreload"]`))n(e);new MutationObserver(e=>{for(let t of e)if(t.type===`childList`)for(let e of t.addedNodes)e.tagName===`LINK`&&e.rel===`modulepreload`&&n(e)}).observe(document,{childList:!0,subtree:!0});function t(e){let t={};return e.integrity&&(t.integrity=e.integrity),e.referrerPolicy&&(t.referrerPolicy=e.referrerPolicy),e.crossOrigin===`use-credentials`?t.credentials=`include`:e.crossOrigin===`anonymous`?t.credentials=`omit`:t.credentials=`same-origin`,t}function n(e){if(e.ep)return;e.ep=!0;let n=t(e);fetch(e.href,n)}})();var e=globalThis,t=e.ShadowRoot&&(e.ShadyCSS===void 0||e.ShadyCSS.nativeShadow)&&`adoptedStyleSheets`in Document.prototype&&`replace`in CSSStyleSheet.prototype,n=Symbol(),r=new WeakMap,i=class{constructor(e,t,r){if(this._$cssResult$=!0,r!==n)throw Error("CSSResult is not constructable. Use `unsafeCSS` or `css` instead.");this.cssText=e,this.t=t}get styleSheet(){let e=this.o,n=this.t;if(t&&e===void 0){let t=n!==void 0&&n.length===1;t&&(e=r.get(n)),e===void 0&&((this.o=e=new CSSStyleSheet).replaceSync(this.cssText),t&&r.set(n,e))}return e}toString(){return this.cssText}},a=e=>new i(typeof e==`string`?e:e+``,void 0,n),o=(e,...t)=>new i(e.length===1?e[0]:t.reduce((t,n,r)=>t+(e=>{if(!0===e._$cssResult$)return e.cssText;if(typeof e==`number`)return e;throw Error(`Value passed to 'css' function must be a 'css' function result: `+e+`. Use 'unsafeCSS' to pass non-literal values, but take care to ensure page security.`)})(n)+e[r+1],e[0]),e,n),s=(n,r)=>{if(t)n.adoptedStyleSheets=r.map(e=>e instanceof CSSStyleSheet?e:e.styleSheet);else for(let t of r){let r=document.createElement(`style`),i=e.litNonce;i!==void 0&&r.setAttribute(`nonce`,i),r.textContent=t.cssText,n.appendChild(r)}},c=t?e=>e:e=>e instanceof CSSStyleSheet?(e=>{let t=``;for(let n of e.cssRules)t+=n.cssText;return a(t)})(e):e,{is:l,defineProperty:u,getOwnPropertyDescriptor:d,getOwnPropertyNames:ee,getOwnPropertySymbols:f,getPrototypeOf:te}=Object,p=globalThis,m=p.trustedTypes,ne=m?m.emptyScript:``,re=p.reactiveElementPolyfillSupport,h=(e,t)=>e,g={toAttribute(e,t){switch(t){case Boolean:e=e?ne:null;break;case Object:case Array:e=e==null?e:JSON.stringify(e)}return e},fromAttribute(e,t){let n=e;switch(t){case Boolean:n=e!==null;break;case Number:n=e===null?null:Number(e);break;case Object:case Array:try{n=JSON.parse(e)}catch{n=null}}return n}},_=(e,t)=>!l(e,t),v={attribute:!0,type:String,converter:g,reflect:!1,useDefault:!1,hasChanged:_};Symbol.metadata??=Symbol(`metadata`),p.litPropertyMetadata??=new WeakMap;var y=class extends HTMLElement{static addInitializer(e){this._$Ei(),(this.l??=[]).push(e)}static get observedAttributes(){return this.finalize(),this._$Eh&&[...this._$Eh.keys()]}static createProperty(e,t=v){if(t.state&&(t.attribute=!1),this._$Ei(),this.prototype.hasOwnProperty(e)&&((t=Object.create(t)).wrapped=!0),this.elementProperties.set(e,t),!t.noAccessor){let n=Symbol(),r=this.getPropertyDescriptor(e,n,t);r!==void 0&&u(this.prototype,e,r)}}static getPropertyDescriptor(e,t,n){let{get:r,set:i}=d(this.prototype,e)??{get(){return this[t]},set(e){this[t]=e}};return{get:r,set(t){let a=r?.call(this);i?.call(this,t),this.requestUpdate(e,a,n)},configurable:!0,enumerable:!0}}static getPropertyOptions(e){return this.elementProperties.get(e)??v}static _$Ei(){if(this.hasOwnProperty(h(`elementProperties`)))return;let e=te(this);e.finalize(),e.l!==void 0&&(this.l=[...e.l]),this.elementProperties=new Map(e.elementProperties)}static finalize(){if(this.hasOwnProperty(h(`finalized`)))return;if(this.finalized=!0,this._$Ei(),this.hasOwnProperty(h(`properties`))){let e=this.properties,t=[...ee(e),...f(e)];for(let n of t)this.createProperty(n,e[n])}let e=this[Symbol.metadata];if(e!==null){let t=litPropertyMetadata.get(e);if(t!==void 0)for(let[e,n]of t)this.elementProperties.set(e,n)}this._$Eh=new Map;for(let[e,t]of this.elementProperties){let n=this._$Eu(e,t);n!==void 0&&this._$Eh.set(n,e)}this.elementStyles=this.finalizeStyles(this.styles)}static finalizeStyles(e){let t=[];if(Array.isArray(e)){let n=new Set(e.flat(1/0).reverse());for(let e of n)t.unshift(c(e))}else e!==void 0&&t.push(c(e));return t}static _$Eu(e,t){let n=t.attribute;return!1===n?void 0:typeof n==`string`?n:typeof e==`string`?e.toLowerCase():void 0}constructor(){super(),this._$Ep=void 0,this.isUpdatePending=!1,this.hasUpdated=!1,this._$Em=null,this._$Ev()}_$Ev(){this._$ES=new Promise(e=>this.enableUpdating=e),this._$AL=new Map,this._$E_(),this.requestUpdate(),this.constructor.l?.forEach(e=>e(this))}addController(e){(this._$EO??=new Set).add(e),this.renderRoot!==void 0&&this.isConnected&&e.hostConnected?.()}removeController(e){this._$EO?.delete(e)}_$E_(){let e=new Map,t=this.constructor.elementProperties;for(let n of t.keys())this.hasOwnProperty(n)&&(e.set(n,this[n]),delete this[n]);e.size>0&&(this._$Ep=e)}createRenderRoot(){let e=this.shadowRoot??this.attachShadow(this.constructor.shadowRootOptions);return s(e,this.constructor.elementStyles),e}connectedCallback(){this.renderRoot??=this.createRenderRoot(),this.enableUpdating(!0),this._$EO?.forEach(e=>e.hostConnected?.())}enableUpdating(e){}disconnectedCallback(){this._$EO?.forEach(e=>e.hostDisconnected?.())}attributeChangedCallback(e,t,n){this._$AK(e,n)}_$ET(e,t){let n=this.constructor.elementProperties.get(e),r=this.constructor._$Eu(e,n);if(r!==void 0&&!0===n.reflect){let i=(n.converter?.toAttribute===void 0?g:n.converter).toAttribute(t,n.type);this._$Em=e,i==null?this.removeAttribute(r):this.setAttribute(r,i),this._$Em=null}}_$AK(e,t){let n=this.constructor,r=n._$Eh.get(e);if(r!==void 0&&this._$Em!==r){let e=n.getPropertyOptions(r),i=typeof e.converter==`function`?{fromAttribute:e.converter}:e.converter?.fromAttribute===void 0?g:e.converter;this._$Em=r;let a=i.fromAttribute(t,e.type);this[r]=a??this._$Ej?.get(r)??a,this._$Em=null}}requestUpdate(e,t,n,r=!1,i){if(e!==void 0){let a=this.constructor;if(!1===r&&(i=this[e]),n??=a.getPropertyOptions(e),!((n.hasChanged??_)(i,t)||n.useDefault&&n.reflect&&i===this._$Ej?.get(e)&&!this.hasAttribute(a._$Eu(e,n))))return;this.C(e,t,n)}!1===this.isUpdatePending&&(this._$ES=this._$EP())}C(e,t,{useDefault:n,reflect:r,wrapped:i},a){n&&!(this._$Ej??=new Map).has(e)&&(this._$Ej.set(e,a??t??this[e]),!0!==i||a!==void 0)||(this._$AL.has(e)||(this.hasUpdated||n||(t=void 0),this._$AL.set(e,t)),!0===r&&this._$Em!==e&&(this._$Eq??=new Set).add(e))}async _$EP(){this.isUpdatePending=!0;try{await this._$ES}catch(e){Promise.reject(e)}let e=this.scheduleUpdate();return e!=null&&await e,!this.isUpdatePending}scheduleUpdate(){return this.performUpdate()}performUpdate(){if(!this.isUpdatePending)return;if(!this.hasUpdated){if(this.renderRoot??=this.createRenderRoot(),this._$Ep){for(let[e,t]of this._$Ep)this[e]=t;this._$Ep=void 0}let e=this.constructor.elementProperties;if(e.size>0)for(let[t,n]of e){let{wrapped:e}=n,r=this[t];!0!==e||this._$AL.has(t)||r===void 0||this.C(t,void 0,n,r)}}let e=!1,t=this._$AL;try{e=this.shouldUpdate(t),e?(this.willUpdate(t),this._$EO?.forEach(e=>e.hostUpdate?.()),this.update(t)):this._$EM()}catch(t){throw e=!1,this._$EM(),t}e&&this._$AE(t)}willUpdate(e){}_$AE(e){this._$EO?.forEach(e=>e.hostUpdated?.()),this.hasUpdated||(this.hasUpdated=!0,this.firstUpdated(e)),this.updated(e)}_$EM(){this._$AL=new Map,this.isUpdatePending=!1}get updateComplete(){return this.getUpdateComplete()}getUpdateComplete(){return this._$ES}shouldUpdate(e){return!0}update(e){this._$Eq&&=this._$Eq.forEach(e=>this._$ET(e,this[e])),this._$EM()}updated(e){}firstUpdated(e){}};y.elementStyles=[],y.shadowRootOptions={mode:`open`},y[h(`elementProperties`)]=new Map,y[h(`finalized`)]=new Map,re?.({ReactiveElement:y}),(p.reactiveElementVersions??=[]).push(`2.1.2`);var b=globalThis,x=e=>e,S=b.trustedTypes,C=S?S.createPolicy(`lit-html`,{createHTML:e=>e}):void 0,w=`$lit$`,T=`lit$${Math.random().toFixed(9).slice(2)}$`,E=`?`+T,ie=`<${E}>`,D=document,O=()=>D.createComment(``),k=e=>e===null||typeof e!=`object`&&typeof e!=`function`,A=Array.isArray,ae=e=>A(e)||typeof e?.[Symbol.iterator]==`function`,j=`[ 	
\f\r]`,M=/<(?:(!--|\/[^a-zA-Z])|(\/?[a-zA-Z][^>\s]*)|(\/?$))/g,N=/-->/g,P=/>/g,F=RegExp(`>|${j}(?:([^\\s"'>=/]+)(${j}*=${j}*(?:[^ \t\n\f\r"'\`<>=]|("|')|))|$)`,`g`),I=/'/g,L=/"/g,R=/^(?:script|style|textarea|title)$/i,z=(e=>(t,...n)=>({_$litType$:e,strings:t,values:n}))(1),B=Symbol.for(`lit-noChange`),V=Symbol.for(`lit-nothing`),H=new WeakMap,U=D.createTreeWalker(D,129);function W(e,t){if(!A(e)||!e.hasOwnProperty(`raw`))throw Error(`invalid template strings array`);return C===void 0?t:C.createHTML(t)}var G=(e,t)=>{let n=e.length-1,r=[],i,a=t===2?`<svg>`:t===3?`<math>`:``,o=M;for(let t=0;t<n;t++){let n=e[t],s,c,l=-1,u=0;for(;u<n.length&&(o.lastIndex=u,c=o.exec(n),c!==null);)u=o.lastIndex,o===M?c[1]===`!--`?o=N:c[1]===void 0?c[2]===void 0?c[3]!==void 0&&(o=F):(R.test(c[2])&&(i=RegExp(`</`+c[2],`g`)),o=F):o=P:o===F?c[0]===`>`?(o=i??M,l=-1):c[1]===void 0?l=-2:(l=o.lastIndex-c[2].length,s=c[1],o=c[3]===void 0?F:c[3]===`"`?L:I):o===L||o===I?o=F:o===N||o===P?o=M:(o=F,i=void 0);let d=o===F&&e[t+1].startsWith(`/>`)?` `:``;a+=o===M?n+ie:l>=0?(r.push(s),n.slice(0,l)+w+n.slice(l)+T+d):n+T+(l===-2?t:d)}return[W(e,a+(e[n]||`<?>`)+(t===2?`</svg>`:t===3?`</math>`:``)),r]},K=class e{constructor({strings:t,_$litType$:n},r){let i;this.parts=[];let a=0,o=0,s=t.length-1,c=this.parts,[l,u]=G(t,n);if(this.el=e.createElement(l,r),U.currentNode=this.el.content,n===2||n===3){let e=this.el.content.firstChild;e.replaceWith(...e.childNodes)}for(;(i=U.nextNode())!==null&&c.length<s;){if(i.nodeType===1){if(i.hasAttributes())for(let e of i.getAttributeNames())if(e.endsWith(w)){let t=u[o++],n=i.getAttribute(e).split(T),r=/([.?@])?(.*)/.exec(t);c.push({type:1,index:a,name:r[2],strings:n,ctor:r[1]===`.`?se:r[1]===`?`?ce:r[1]===`@`?le:Y}),i.removeAttribute(e)}else e.startsWith(T)&&(c.push({type:6,index:a}),i.removeAttribute(e));if(R.test(i.tagName)){let e=i.textContent.split(T),t=e.length-1;if(t>0){i.textContent=S?S.emptyScript:``;for(let n=0;n<t;n++)i.append(e[n],O()),U.nextNode(),c.push({type:2,index:++a});i.append(e[t],O())}}}else if(i.nodeType===8)if(i.data===E)c.push({type:2,index:a});else{let e=-1;for(;(e=i.data.indexOf(T,e+1))!==-1;)c.push({type:7,index:a}),e+=T.length-1}a++}}static createElement(e,t){let n=D.createElement(`template`);return n.innerHTML=e,n}};function q(e,t,n=e,r){if(t===B)return t;let i=r===void 0?n._$Cl:n._$Co?.[r],a=k(t)?void 0:t._$litDirective$;return i?.constructor!==a&&(i?._$AO?.(!1),a===void 0?i=void 0:(i=new a(e),i._$AT(e,n,r)),r===void 0?n._$Cl=i:(n._$Co??=[])[r]=i),i!==void 0&&(t=q(e,i._$AS(e,t.values),i,r)),t}var oe=class{constructor(e,t){this._$AV=[],this._$AN=void 0,this._$AD=e,this._$AM=t}get parentNode(){return this._$AM.parentNode}get _$AU(){return this._$AM._$AU}u(e){let{el:{content:t},parts:n}=this._$AD,r=(e?.creationScope??D).importNode(t,!0);U.currentNode=r;let i=U.nextNode(),a=0,o=0,s=n[0];for(;s!==void 0;){if(a===s.index){let t;s.type===2?t=new J(i,i.nextSibling,this,e):s.type===1?t=new s.ctor(i,s.name,s.strings,this,e):s.type===6&&(t=new ue(i,this,e)),this._$AV.push(t),s=n[++o]}a!==s?.index&&(i=U.nextNode(),a++)}return U.currentNode=D,r}p(e){let t=0;for(let n of this._$AV)n!==void 0&&(n.strings===void 0?n._$AI(e[t]):(n._$AI(e,n,t),t+=n.strings.length-2)),t++}},J=class e{get _$AU(){return this._$AM?._$AU??this._$Cv}constructor(e,t,n,r){this.type=2,this._$AH=V,this._$AN=void 0,this._$AA=e,this._$AB=t,this._$AM=n,this.options=r,this._$Cv=r?.isConnected??!0}get parentNode(){let e=this._$AA.parentNode,t=this._$AM;return t!==void 0&&e?.nodeType===11&&(e=t.parentNode),e}get startNode(){return this._$AA}get endNode(){return this._$AB}_$AI(e,t=this){e=q(this,e,t),k(e)?e===V||e==null||e===``?(this._$AH!==V&&this._$AR(),this._$AH=V):e!==this._$AH&&e!==B&&this._(e):e._$litType$===void 0?e.nodeType===void 0?ae(e)?this.k(e):this._(e):this.T(e):this.$(e)}O(e){return this._$AA.parentNode.insertBefore(e,this._$AB)}T(e){this._$AH!==e&&(this._$AR(),this._$AH=this.O(e))}_(e){this._$AH!==V&&k(this._$AH)?this._$AA.nextSibling.data=e:this.T(D.createTextNode(e)),this._$AH=e}$(e){let{values:t,_$litType$:n}=e,r=typeof n==`number`?this._$AC(e):(n.el===void 0&&(n.el=K.createElement(W(n.h,n.h[0]),this.options)),n);if(this._$AH?._$AD===r)this._$AH.p(t);else{let e=new oe(r,this),n=e.u(this.options);e.p(t),this.T(n),this._$AH=e}}_$AC(e){let t=H.get(e.strings);return t===void 0&&H.set(e.strings,t=new K(e)),t}k(t){A(this._$AH)||(this._$AH=[],this._$AR());let n=this._$AH,r,i=0;for(let a of t)i===n.length?n.push(r=new e(this.O(O()),this.O(O()),this,this.options)):r=n[i],r._$AI(a),i++;i<n.length&&(this._$AR(r&&r._$AB.nextSibling,i),n.length=i)}_$AR(e=this._$AA.nextSibling,t){for(this._$AP?.(!1,!0,t);e!==this._$AB;){let t=x(e).nextSibling;x(e).remove(),e=t}}setConnected(e){this._$AM===void 0&&(this._$Cv=e,this._$AP?.(e))}},Y=class{get tagName(){return this.element.tagName}get _$AU(){return this._$AM._$AU}constructor(e,t,n,r,i){this.type=1,this._$AH=V,this._$AN=void 0,this.element=e,this.name=t,this._$AM=r,this.options=i,n.length>2||n[0]!==``||n[1]!==``?(this._$AH=Array(n.length-1).fill(new String),this.strings=n):this._$AH=V}_$AI(e,t=this,n,r){let i=this.strings,a=!1;if(i===void 0)e=q(this,e,t,0),a=!k(e)||e!==this._$AH&&e!==B,a&&(this._$AH=e);else{let r=e,o,s;for(e=i[0],o=0;o<i.length-1;o++)s=q(this,r[n+o],t,o),s===B&&(s=this._$AH[o]),a||=!k(s)||s!==this._$AH[o],s===V?e=V:e!==V&&(e+=(s??``)+i[o+1]),this._$AH[o]=s}a&&!r&&this.j(e)}j(e){e===V?this.element.removeAttribute(this.name):this.element.setAttribute(this.name,e??``)}},se=class extends Y{constructor(){super(...arguments),this.type=3}j(e){this.element[this.name]=e===V?void 0:e}},ce=class extends Y{constructor(){super(...arguments),this.type=4}j(e){this.element.toggleAttribute(this.name,!!e&&e!==V)}},le=class extends Y{constructor(e,t,n,r,i){super(e,t,n,r,i),this.type=5}_$AI(e,t=this){if((e=q(this,e,t,0)??V)===B)return;let n=this._$AH,r=e===V&&n!==V||e.capture!==n.capture||e.once!==n.once||e.passive!==n.passive,i=e!==V&&(n===V||r);r&&this.element.removeEventListener(this.name,this,n),i&&this.element.addEventListener(this.name,this,e),this._$AH=e}handleEvent(e){typeof this._$AH==`function`?this._$AH.call(this.options?.host??this.element,e):this._$AH.handleEvent(e)}},ue=class{constructor(e,t,n){this.element=e,this.type=6,this._$AN=void 0,this._$AM=t,this.options=n}get _$AU(){return this._$AM._$AU}_$AI(e){q(this,e)}},de=b.litHtmlPolyfillSupport;de?.(K,J),(b.litHtmlVersions??=[]).push(`3.3.2`);var fe=(e,t,n)=>{let r=n?.renderBefore??t,i=r._$litPart$;if(i===void 0){let e=n?.renderBefore??null;r._$litPart$=i=new J(t.insertBefore(O(),e),e,void 0,n??{})}return i._$AI(e),i},X=globalThis,Z=class extends y{constructor(){super(...arguments),this.renderOptions={host:this},this._$Do=void 0}createRenderRoot(){let e=super.createRenderRoot();return this.renderOptions.renderBefore??=e.firstChild,e}update(e){let t=this.render();this.hasUpdated||(this.renderOptions.isConnected=this.isConnected),super.update(e),this._$Do=fe(t,this.renderRoot,this.renderOptions)}connectedCallback(){super.connectedCallback(),this._$Do?.setConnected(!0)}disconnectedCallback(){super.disconnectedCallback(),this._$Do?.setConnected(!1)}render(){return B}};Z._$litElement$=!0,Z.finalized=!0,X.litElementHydrateSupport?.({LitElement:Z});var pe=X.litElementPolyfillSupport;pe?.({LitElement:Z}),(X.litElementVersions??=[]).push(`4.2.2`);var me=class extends Z{static styles=o`
    :host {
      position: fixed;
      inset: 0;
      z-index: 0;
      pointer-events: none;
    }

    canvas {
      width: 100%;
      height: 100%;
      display: block;
    }
  `;canvas;ctx;dpr=1;raf=0;particles=[];pointer={x:-9999,y:-9999,active:!1};render(){return z`<canvas></canvas>`}firstUpdated(){let e=this.renderRoot.querySelector(`canvas`);if(!(e instanceof HTMLCanvasElement))return;let t=e.getContext(`2d`);t&&(this.canvas=e,this.ctx=t,this.dpr=Math.max(1,Math.min(2,window.devicePixelRatio||1)),this.resize(),this.seed(),window.addEventListener(`resize`,this.onResize,{passive:!0}),window.addEventListener(`pointermove`,this.onPointerMove,{passive:!0}),window.addEventListener(`pointerdown`,this.onPointerDown,{passive:!0}),window.addEventListener(`pointerleave`,this.onPointerLeave,{passive:!0}),this.raf=window.requestAnimationFrame(this.tick))}disconnectedCallback(){super.disconnectedCallback(),window.removeEventListener(`resize`,this.onResize),window.removeEventListener(`pointermove`,this.onPointerMove),window.removeEventListener(`pointerdown`,this.onPointerDown),window.removeEventListener(`pointerleave`,this.onPointerLeave),this.raf&&window.cancelAnimationFrame(this.raf)}onResize=()=>this.resize();onPointerMove=e=>{this.pointer.x=e.clientX,this.pointer.y=e.clientY,this.pointer.active=!0};onPointerDown=e=>{this.pointer.x=e.clientX,this.pointer.y=e.clientY,this.pointer.active=!0,this.burst(e.clientX,e.clientY)};onPointerLeave=()=>{this.pointer.active=!1,this.pointer.x=-9999,this.pointer.y=-9999};resize(){if(!this.canvas||!this.ctx)return;let e=Math.max(1,window.innerWidth),t=Math.max(1,window.innerHeight);this.canvas.width=Math.floor(e*this.dpr),this.canvas.height=Math.floor(t*this.dpr),this.ctx.setTransform(this.dpr,0,0,this.dpr,0,0);let n=Math.floor(Math.min(140,Math.max(70,e*t/16e3)));this.particles.length>n&&(this.particles.length=n),this.particles.length<n&&this.addParticles(n-this.particles.length)}seed(){this.particles=[];let e=Math.max(1,window.innerWidth),t=Math.max(1,window.innerHeight),n=Math.floor(Math.min(140,Math.max(70,e*t/16e3)));for(let r=0;r<n;r++)this.particles.push(this.newParticle(e,t))}addParticles(e){let t=Math.max(1,window.innerWidth),n=Math.max(1,window.innerHeight);for(let r=0;r<e;r++)this.particles.push(this.newParticle(t,n))}newParticle(e,t){let n=.22+Math.random()*.55,r=Math.random()*Math.PI*2;return{x:Math.random()*e,y:Math.random()*t,vx:Math.cos(r)*n,vy:Math.sin(r)*n,r:1.2+Math.random()*1.9,hue:Math.random()<.55?185+Math.random()*25:255+Math.random()*25}}burst(e,t){let n=Math.max(1,window.innerWidth),r=Math.max(1,window.innerHeight);for(let i=0;i<10;i++){let i=this.newParticle(n,r);i.x=e+(Math.random()-.5)*20,i.y=t+(Math.random()-.5)*20;let a=Math.random()*Math.PI*2,o=.9+Math.random()*1.4;i.vx=Math.cos(a)*o,i.vy=Math.sin(a)*o,i.r=1.1+Math.random()*2.6,this.particles.push(i)}let i=Math.floor(Math.min(180,Math.max(90,n*r/14e3)));this.particles.length>i&&this.particles.splice(0,this.particles.length-i)}tick=()=>{this.raf=window.requestAnimationFrame(this.tick),this.draw()};draw(){let e=this.ctx;if(!e)return;let t=Math.max(1,window.innerWidth),n=Math.max(1,window.innerHeight);e.clearRect(0,0,t,n);let r=this.pointer.x,i=this.pointer.y,a=this.pointer.active?140:0,o=a*a;for(let e of this.particles){if(e.x+=e.vx,e.y+=e.vy,e.x<-20&&(e.x=t+20),e.x>t+20&&(e.x=-20),e.y<-20&&(e.y=n+20),e.y>n+20&&(e.y=-20),a>0){let t=e.x-r,n=e.y-i,s=t*t+n*n;if(s<o){let r=Math.max(1,Math.sqrt(s)),i=(1-r/a)*.045;e.vx+=-t/r*i,e.vy+=-n/r*i}}let s=Math.sqrt(e.vx*e.vx+e.vy*e.vy),c=1.15;s>c&&(e.vx=e.vx/s*c,e.vy=e.vy/s*c)}for(let t=0;t<this.particles.length;t++){let n=this.particles[t];for(let r=t+1;r<this.particles.length;r++){let t=this.particles[r],i=n.x-t.x,a=n.y-t.y,o=i*i+a*a;if(o>25600)continue;let s=Math.max(0,.16-Math.sqrt(o)/1e3);s<=0||(e.strokeStyle=`hsla(${(n.hue+t.hue)*.5}, 95%, 65%, ${s})`,e.lineWidth=1,e.beginPath(),e.moveTo(n.x,n.y),e.lineTo(t.x,t.y),e.stroke())}}for(let t of this.particles)e.fillStyle=`hsla(${t.hue}, 95%, 70%, 0.70)`,e.beginPath(),e.arc(t.x,t.y,t.r,0,Math.PI*2),e.fill();let s=e.createRadialGradient(r,i,0,r,i,220);s.addColorStop(0,`rgba(0,229,255,0.09)`),s.addColorStop(1,`rgba(0,229,255,0)`),e.fillStyle=s,e.fillRect(0,0,t,n)}};customElements.define(`cp-particle-background`,me);async function Q(e){let t=await fetch(e,{headers:{Accept:`application/json`}});if(!t.ok)throw Error(`http ${t.status}`);return await t.json()}async function he(e,t){let n=await fetch(e,{method:`POST`,headers:{Accept:`application/json`,"Content-Type":`application/json`},body:JSON.stringify(t)});if(!n.ok)throw Error(`http ${n.status}`);return await n.json()}var $={health:()=>Q(`/api/health`),servers:()=>Q(`/api/servers`),routingQuery:e=>{let t=new URLSearchParams;e.strategy&&t.set(`strategy`,e.strategy),e.region&&t.set(`region`,e.region),e.limit!=null&&t.set(`limit`,String(e.limit));let n=t.toString();return Q(`/api/routing/query${n?`?${n}`:``}`)},gmCommands:()=>Q(`/api/gm/commands`),gmExecute:e=>he(`/api/gm/execute`,{cmd:e})},ge=class extends Z{static styles=o`
    :host {
      display: block;
      min-height: 100vh;
      color: rgba(231, 240, 255, 0.92);
      font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial,
        'Apple Color Emoji', 'Segoe UI Emoji';
      background: radial-gradient(1200px 800px at 30% 20%, rgba(0, 229, 255, 0.10), transparent 55%),
        radial-gradient(900px 700px at 80% 70%, rgba(124, 77, 255, 0.12), transparent 55%),
        #050814;
      overflow: hidden;
    }

    .frame {
      position: relative;
      min-height: 100vh;
    }

    .topbar {
      position: sticky;
      top: 0;
      z-index: 5;
      height: 56px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 18px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
      background: linear-gradient(
        to bottom,
        rgba(8, 12, 26, 0.90),
        rgba(8, 12, 26, 0.65)
      );
      backdrop-filter: blur(10px);
    }

    .brand {
      display: flex;
      align-items: baseline;
      gap: 10px;
      letter-spacing: 0.5px;
    }

    .brand .title {
      font-weight: 700;
      font-size: 15px;
    }

    .brand .sub {
      font-size: 12px;
      color: rgba(170, 200, 255, 0.7);
    }

    .grid {
      position: relative;
      display: grid;
      grid-template-columns: 260px 1fr;
      min-height: calc(100vh - 56px);
    }

    .sidebar {
      position: sticky;
      top: 56px;
      height: calc(100vh - 56px);
      padding: 14px;
      border-right: 1px solid rgba(255, 255, 255, 0.06);
      background: linear-gradient(
        to bottom,
        rgba(8, 12, 26, 0.70),
        rgba(8, 12, 26, 0.35)
      );
      backdrop-filter: blur(10px);
    }

    .nav {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-top: 10px;
    }

    .nav button {
      all: unset;
      cursor: pointer;
      user-select: none;
      border-radius: 12px;
      padding: 10px 12px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      background: rgba(255, 255, 255, 0.03);
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease,
        background 160ms ease;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .nav button:hover {
      transform: translateY(-1px);
      border-color: rgba(0, 229, 255, 0.28);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 16px 40px rgba(0, 0, 0, 0.35);
      background: rgba(0, 229, 255, 0.05);
    }

    .nav button[data-active='true'] {
      border-color: rgba(124, 77, 255, 0.38);
      box-shadow: 0 0 0 1px rgba(124, 77, 255, 0.18), 0 18px 50px rgba(0, 0, 0, 0.42);
      background: linear-gradient(135deg, rgba(124, 77, 255, 0.18), rgba(0, 229, 255, 0.10));
    }

    .nav .label {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .nav .label b {
      font-size: 13px;
      font-weight: 650;
    }

    .nav .label span {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.66);
    }

    .nav .chip {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
      font-size: 11px;
      padding: 2px 8px;
      border-radius: 999px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      color: rgba(210, 230, 255, 0.82);
      background: rgba(255, 255, 255, 0.03);
    }

    .content {
      position: relative;
      padding: 18px;
    }

    .panel {
      border-radius: 18px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: linear-gradient(
        to bottom right,
        rgba(255, 255, 255, 0.06),
        rgba(255, 255, 255, 0.03)
      );
      backdrop-filter: blur(14px);
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.45);
      overflow: hidden;
    }

    .panelHeader {
      padding: 16px 16px 12px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 12px;
    }

    .panelHeader h1 {
      margin: 0;
      font-size: 16px;
      letter-spacing: 0.3px;
    }

    .panelHeader p {
      margin: 0;
      font-size: 12px;
      color: rgba(170, 200, 255, 0.7);
    }

    .statusLine {
      min-height: 16px;
    }

    .panelBody {
      padding: 16px;
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
    }

    .row {
      display: flex;
      gap: 10px;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
    }

    .input {
      all: unset;
      display: inline-flex;
      align-items: center;
      padding: 10px 12px;
      border-radius: 12px;
      border: 1px solid rgba(255, 255, 255, 0.10);
      background: rgba(255, 255, 255, 0.04);
      color: rgba(231, 240, 255, 0.92);
      min-width: 260px;
    }

    .input::placeholder {
      color: rgba(170, 200, 255, 0.55);
    }

    .btn {
      all: unset;
      cursor: pointer;
      user-select: none;
      border-radius: 12px;
      padding: 10px 12px;
      border: 1px solid rgba(255, 255, 255, 0.10);
      background: rgba(255, 255, 255, 0.04);
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease,
        background 160ms ease;
    }

    .btn:hover {
      transform: translateY(-1px);
      border-color: rgba(0, 229, 255, 0.28);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 16px 40px rgba(0, 0, 0, 0.35);
      background: rgba(0, 229, 255, 0.05);
    }

    .mono {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
    }

    pre.out {
      margin: 0;
      padding: 12px;
      border-radius: 14px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: rgba(0, 0, 0, 0.25);
      overflow: auto;
      max-height: 360px;
      white-space: pre-wrap;
      word-break: break-word;
    }

    .card {
      border-radius: 16px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      background: rgba(3, 6, 18, 0.35);
      padding: 14px;
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease;
    }

    .card:hover {
      transform: translateY(-2px);
      border-color: rgba(0, 229, 255, 0.25);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 20px 60px rgba(0, 0, 0, 0.4);
    }

    .metric {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .metric .k {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.72);
    }

    .metric .v {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
      font-size: 22px;
      letter-spacing: 0.4px;
    }

    .metric .hint {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.6);
    }

    @media (max-width: 980px) {
      .grid {
        grid-template-columns: 1fr;
      }

      .sidebar {
        position: relative;
        top: 0;
        height: auto;
      }

      .panelBody {
        grid-template-columns: 1fr;
      }
    }
  `;static properties={nav:{state:!0},serversRes:{state:!0},routingRes:{state:!0},gmRes:{state:!0},gmSearch:{state:!0},gmCmdText:{state:!0},gmExecLoading:{state:!0},gmExecOut:{state:!0},error:{state:!0},lastUpdated:{state:!0},loading:{state:!0}};refreshTimer=0;constructor(){super(),this.nav=`overview`,this.loading=!1,this.gmSearch=``,this.gmCmdText=``,this.gmExecLoading=!1}setNav(e){this.nav=e,e===`gm`&&this.loadGmCommands()}connectedCallback(){super.connectedCallback(),this.refresh(),this.refreshTimer=window.setInterval(()=>this.refresh(),3e3)}disconnectedCallback(){super.disconnectedCallback(),this.refreshTimer&&window.clearInterval(this.refreshTimer)}async refresh(){this.loading=!0;try{let[e,t]=await Promise.all([$.servers(),$.routingQuery({strategy:`load`,limit:12})]);this.serversRes=e,this.routingRes=t,this.error=void 0,this.lastUpdated=new Date().toLocaleTimeString()}catch(e){this.error=e instanceof Error?e.message:String(e)}finally{this.loading=!1}}async loadGmCommands(){if(!(this.gmRes?.ok&&this.gmRes.result?.items?.length))try{this.gmRes=await $.gmCommands()}catch(e){this.gmRes={ok:!1,error:e instanceof Error?e.message:String(e)}}}setGmCmdText(e){this.gmCmdText=e}async executeGm(){let e=this.gmCmdText.trim();if(e){this.gmExecLoading=!0;try{let t=await $.gmExecute(e);this.gmExecOut=JSON.stringify(t,null,2)}catch(e){this.gmExecOut=JSON.stringify({ok:!1,error:e instanceof Error?e.message:String(e)},null,2)}finally{this.gmExecLoading=!1}}}renderNavButton(e,t,n,r){return z`
      <button data-active=${this.nav===e} @click=${()=>this.setNav(e)}>
        <div class="label">
          <b>${t}</b>
          <span>${n}</span>
        </div>
        <div class="chip">${r}</div>
      </button>
    `}headerText(){return this.nav===`overview`?{h:`总览`,p:`服务健康与选服候选`}:this.nav===`servers`?{h:`实例`,p:`服务健康明细`}:this.nav===`routing`?{h:`分流`,p:`目录服务选服结果（load 策略）`}:{h:`GM`,p:`指令列表、分组与检索`}}renderOverview(){let e=this.serversRes,t=this.routingRes,n=e?.total??0,r=e?.healthy??0,i=Math.max(0,n-r),a=t?.servers?.length??0;return z`
      <div class="panelBody">
        <div class="card">
          <div class="metric">
            <div class="k">服务数量</div>
            <div class="v">${n||`—`}</div>
            <div class="hint">基于配置 + 健康检查聚合</div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k">健康实例</div>
            <div class="v">${e?r:`—`}</div>
            <div class="hint">异常实例：${e?i:`—`}</div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k">逻辑服候选</div>
            <div class="v">${t?a:`—`}</div>
            <div class="hint">Directory：query_servers(load)</div>
          </div>
        </div>
      </div>
    `}renderServers(){let e=this.serversRes?.items??[];if(e.length===0)return z`<div class="panelBody"><div class="card">暂无数据</div></div>`;let t=this.routingRes?.servers??[];return z`
      <div class="panelBody" style="grid-template-columns: 1fr;">
        <div class="card">
          <div class="mono">服务健康</div>
        </div>
        <div class="panelBody" style="padding: 0; grid-template-columns: repeat(2, minmax(0, 1fr));">
          ${e.map(e=>z`
              <div class="card">
                <div class="metric">
                  <div class="k">${e.id} • ${e.type} • ${e.region}</div>
                  <div class="v">${e.health.ok?`正常`:`异常`}</div>
                  <div class="hint">
                    ${e.host}:${e.tcpPort} | 健康端口:${e.healthPort} | ${e.health.latencyMs}ms
                    ${e.health.error?` | ${e.health.error}`:``}
                  </div>
                </div>
              </div>
            `)}
        </div>
        <div class="card">
          <div class="mono">目录服务返回的逻辑服（load）</div>
        </div>
        <div class="panelBody" style="padding: 0; grid-template-columns: repeat(2, minmax(0, 1fr));">
          ${t.length===0?z`<div class="card">暂无数据</div>`:t.map(e=>z`
                  <div class="card">
                    <div class="metric">
                      <div class="k">${e.id} • ${e.region}</div>
                      <div class="v">${e.playerCount}/${e.capacity}</div>
                      <div class="hint">${e.host}:${e.port} | 状态=${e.status} | RTT=${e.avgRttMs}ms</div>
                    </div>
                  </div>
                `)}
        </div>
      </div>
    `}renderRouting(){let e=this.routingRes?.servers??[];return e.length===0?z`<div class="panelBody"><div class="card">暂无数据</div></div>`:z`
      <div class="panelBody" style="grid-template-columns: repeat(2, minmax(0, 1fr));">
        ${e.map(e=>z`
            <div class="card">
              <div class="metric">
                <div class="k">${e.id} • ${e.region}</div>
                <div class="v">${e.playerCount}/${e.capacity}</div>
                <div class="hint">
                  ${e.host}:${e.port} | 状态=${e.status} | RTT=${e.avgRttMs}ms
                </div>
              </div>
            </div>
          `)}
      </div>
    `}renderGm(){let e=this.gmRes?.result?.items??[],t=this.gmSearch.trim().toLowerCase(),n=t?e.filter(e=>e.name.toLowerCase().includes(t)?!0:e.aliases?.some(e=>e.toLowerCase().includes(t))??!1):e,r=new Map;for(let e of n){let t=(e.name.split(` `)[0]||e.name).toLowerCase(),n=r.get(t)??[];n.push(e),r.set(t,n)}let i=Array.from(r.keys()).sort((e,t)=>e.localeCompare(t));return z`
      <div class="panelBody" style="grid-template-columns: 1fr;">
        <div class="card">
          <div class="row">
            <input
              class="input mono"
              placeholder="按名称/别名搜索 GM 指令…"
              .value=${this.gmSearch}
              @input=${e=>this.gmSearch=e.target.value}
            />
            <input
              class="input mono"
              style="min-width: 420px;"
              placeholder="输入 GM 指令，例如：status / handlers / blacklist list"
              .value=${this.gmCmdText}
              @input=${e=>this.setGmCmdText(e.target.value)}
              @keydown=${e=>{e.key===`Enter`&&!e.shiftKey&&this.executeGm()}}
            />
            <button class="btn" @click=${()=>this.executeGm()}>
              ${this.gmExecLoading?`执行中…`:`执行`}
            </button>
            <button class="btn" @click=${()=>(this.gmRes=void 0,this.loadGmCommands())}>刷新指令</button>
            <div class="mono">
              ${e.length?`共 ${e.length} 条`:this.gmRes?`暂无指令`:`加载中…`}
            </div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k mono">执行结果</div>
            <pre class="out mono">${this.gmExecOut??`尚未执行`}</pre>
          </div>
        </div>
        ${this.gmRes?.ok===!1?z`<div class="card">加载失败：${this.gmRes.error??`unknown`}</div>`:``}
        ${i.map(e=>{let t=r.get(e)??[];return z`
            <div class="card">
              <div class="row">
                <div class="mono">${e}</div>
                <div class="mono">(${t.length})</div>
              </div>
              <div style="margin-top: 10px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px;">
                ${t.sort((e,t)=>e.name.localeCompare(t.name)).map(e=>z`
                      <div class="card" style="padding: 12px;">
                        <div class="metric">
                          <div class="k mono">
                            <button class="btn mono" style="padding: 6px 10px;" @click=${()=>this.setGmCmdText(e.name)}>
                              ${e.name}
                            </button>
                          </div>
                          <div class="hint mono">
                            ${e.aliases?.length?`别名：${e.aliases.join(` / `)}`:`别名：—`}
                          </div>
                        </div>
                      </div>
                    `)}
              </div>
            </div>
          `})}
      </div>
    `}render(){let e=this.headerText();return z`
      <div class="frame">
        <cp-particle-background></cp-particle-background>
        <div class="topbar">
          <div class="brand">
            <div class="title">ControlPlane</div>
            <div class="sub">运维 • 可观测 • 分流</div>
          </div>
          <div class="sub">自动刷新 3s</div>
        </div>
        <div class="grid">
          <aside class="sidebar">
            <div class="sub">导航</div>
            <div class="nav">
              ${this.renderNavButton(`overview`,`总览`,`服务概览`,`概览`)}
              ${this.renderNavButton(`servers`,`实例`,`健康明细`,`实例`)}
              ${this.renderNavButton(`routing`,`分流`,`选服结果`,`分流`)}
              ${this.renderNavButton(`gm`,`GM`,`指令查询`,`GM`)}
            </div>
          </aside>
          <main class="content">
            <section class="panel">
              <header class="panelHeader">
                <div>
                  <h1>${e.h}</h1>
                  <p class="statusLine">${e.p}</p>
                </div>
                <p class="mono statusLine">
                  ${this.lastUpdated?`更新 ${this.lastUpdated}`:`等待首次更新`}
                  ${this.error?` | 错误：${this.error}`:``}
                </p>
              </header>
              ${this.nav===`overview`?this.renderOverview():this.nav===`servers`?this.renderServers():this.nav===`routing`?this.renderRouting():this.renderGm()}
            </section>
          </main>
        </div>
      </div>
    `}};customElements.define(`cp-app`,ge);