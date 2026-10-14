import { Navigate, Route, Routes, useLocation, useParams } from 'react-router'
import { BomPage } from './routes/BomPage'
import { CataloguePage } from './routes/CataloguePage'
import { NewRackPage } from './routes/NewRackPage'
import { EditSubmainPage, NewSubmainPage } from './routes/SubmainForm'
import { NotFoundPage } from './routes/NotFoundPage'
import { PanelPage } from './routes/PanelPage'
import { ProjectPage } from './routes/ProjectPage'
import { ProjectsPage } from './routes/ProjectsPage'
import { RackPage } from './routes/RackPage'

function MovedTo({ to }: { to: (params: Readonly<Record<string, string | undefined>>) => string }) {
  // The query and fragment go along too, so a link saved with either still lands where it pointed.
  const { search, hash } = useLocation()
  return <Navigate to={to(useParams()) + search + hash} replace />
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectsPage />} />
      <Route path="/catalogue" element={<CataloguePage />} />
      <Route path="/projects/:projectId" element={<ProjectPage />} />
      <Route path="/projects/:projectId/bom" element={<BomPage />} />
      <Route path="/projects/:projectId/panels/new" element={<NewSubmainPage />} />
      <Route path="/projects/:projectId/racks/new" element={<NewRackPage />} />
      <Route path="/panels/:submainId" element={<PanelPage />} />
      <Route path="/panels/:submainId/edit" element={<EditSubmainPage />} />
      <Route path="/panels/:submainId/bom" element={<BomPage />} />
      <Route path="/racks/:rackId" element={<RackPage />} />
      <Route path="/racks/:rackId/bom" element={<BomPage />} />
      {/* The addresses from before the screens said "automation panel". People
          have these bookmarked, so each one lands on its new equivalent. */}
      <Route path="/projects/:projectId/submains/new" element={<MovedTo to={p => `/projects/${p.projectId}/panels/new`} />} />
      <Route path="/submains/:submainId/panel" element={<MovedTo to={p => `/panels/${p.submainId}`} />} />
      <Route path="/submains/:submainId/edit" element={<MovedTo to={p => `/panels/${p.submainId}/edit`} />} />
      <Route path="/submains/:submainId/bom" element={<MovedTo to={p => `/panels/${p.submainId}/bom`} />} />
      {/* Without this an unknown address renders nothing at all, which reads as
          a broken app rather than a wrong link. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
